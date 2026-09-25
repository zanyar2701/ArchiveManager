using System.Collections.Generic;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.Data;

namespace ArchiveManager.Application.Services;

public sealed record SearchFilters(
    string? FreeText = null,
    int? CompanyId = null,
    int? ArchiveLevelId = null,
    int? DocumentTypeId = null,
    int? SubjectId = null,
    int? Year = null,
    string? Extension = null);

/// <summary>
/// Quick search (FreeText only, spec §10) and advanced filtered search share
/// this one implementation. Quick search hits the FTS5 index; advanced search
/// adds precise equality filters on top via a normal joined query.
/// </summary>
public sealed class SearchService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SearchService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public List<Document> Search(SearchFilters filters, int maxResults = 200)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();

        var sql = @"
SELECT d.DocumentId, d.CompanyId, d.DocumentTypeId, d.SubjectId, d.Year, d.Version,
       d.FileFormatId, d.FileName, d.RelativePath, d.SizeBytes, d.RegisteredAt, d.IsMissing,
       c.Name as CompanyName, t.Name as TypeName, s.Name as SubjectName, f.Label as FormatLabel
FROM Documents d
JOIN Companies c ON c.CompanyId = d.CompanyId
JOIN DocumentTypes t ON t.DocumentTypeId = d.DocumentTypeId
LEFT JOIN DocumentSubjects s ON s.SubjectId = d.SubjectId
JOIN FileFormats f ON f.FileFormatId = d.FileFormatId
WHERE 1=1";

        if (!string.IsNullOrWhiteSpace(filters.FreeText))
        {
            // Matches the document's own FTS text, OR any name the document's
            // company has EVER been known by, OR the company's stable archive
            // code — so a search for "محمد احمدی" still finds documents filed
            // under a company that was later renamed from that person's name
            // (entity-identity addendum: "search must handle old and new names").
            sql += @" AND (
                d.DocumentId IN (SELECT rowid FROM DocumentsFTS WHERE DocumentsFTS MATCH $freeText)
                OR d.CompanyId IN (SELECT EntityId FROM EntityNameHistory WHERE Name LIKE $likePattern)
                OR c.ArchiveCode = $exactText
            )";
        }
        if (filters.CompanyId is not null) sql += " AND d.CompanyId = $companyId";
        if (filters.ArchiveLevelId is not null) sql += " AND c.ArchiveLevelId = $levelId";
        if (filters.DocumentTypeId is not null) sql += " AND d.DocumentTypeId = $typeId";
        if (filters.SubjectId is not null) sql += " AND d.SubjectId = $subjectId";
        if (filters.Year is not null) sql += " AND d.Year = $year";
        if (!string.IsNullOrWhiteSpace(filters.Extension)) sql += " AND f.Extension = $ext";

        sql += " ORDER BY d.RegisteredAt DESC LIMIT $max;";
        cmd.CommandText = sql;

        if (!string.IsNullOrWhiteSpace(filters.FreeText))
        {
            // FTS5 default tokenizer: quote the phrase and OR each term so
            // "قرارداد استقرار 1404" matches rows containing any of the tokens,
            // ranked by bm25 implicitly via MATCH — good enough at this scale.
            var escaped = filters.FreeText.Replace("\"", "\"\"");
            var terms = escaped.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var ftsQuery = terms.Length > 0 ? string.Join(" OR ", terms) : "\"\"";
            cmd.Parameters.AddWithValue("$freeText", ftsQuery);
            cmd.Parameters.AddWithValue("$likePattern", $"%{filters.FreeText.Trim()}%");
            cmd.Parameters.AddWithValue("$exactText", filters.FreeText.Trim());
        }
        if (filters.CompanyId is not null) cmd.Parameters.AddWithValue("$companyId", filters.CompanyId);
        if (filters.ArchiveLevelId is not null) cmd.Parameters.AddWithValue("$levelId", filters.ArchiveLevelId);
        if (filters.DocumentTypeId is not null) cmd.Parameters.AddWithValue("$typeId", filters.DocumentTypeId);
        if (filters.SubjectId is not null) cmd.Parameters.AddWithValue("$subjectId", filters.SubjectId);
        if (filters.Year is not null) cmd.Parameters.AddWithValue("$year", filters.Year);
        if (!string.IsNullOrWhiteSpace(filters.Extension)) cmd.Parameters.AddWithValue("$ext", filters.Extension);
        cmd.Parameters.AddWithValue("$max", maxResults);

        var results = new List<Document>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Document
            {
                DocumentId = reader.GetInt32(0),
                CompanyId = reader.GetInt32(1),
                DocumentTypeId = reader.GetInt32(2),
                SubjectId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Year = reader.GetInt32(4),
                Version = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                FileFormatId = reader.GetInt32(6),
                FileName = reader.GetString(7),
                RelativePath = reader.GetString(8),
                SizeBytes = reader.GetInt64(9),
                RegisteredAt = System.DateTime.Parse(reader.GetString(10)),
                IsMissing = reader.GetInt32(11) == 1,
                CompanyName = reader.GetString(12),
                DocumentTypeName = reader.GetString(13),
                SubjectName = reader.IsDBNull(14) ? null : reader.GetString(14),
                FormatLabel = reader.GetString(15)
            });
        }
        return results;
    }

    /// <summary>
    /// Entity-level search (as opposed to document-level Search() above) — used
    /// by the search overlay to show a result like: "شرکت فناوران کردستان،
    /// نوع فعلی: شرکت، مرحله: رشد، نام قبلی: محمد احمدی" when the query matches
    /// a previous name, current name, or archive code (addendum "SEARCH MUST
    /// HANDLE OLD AND NEW NAMES" + worked example).
    /// </summary>
    public List<EntitySearchResult> SearchEntities(string freeText, int maxResults = 20)
    {
        var results = new List<EntitySearchResult>();
        if (string.IsNullOrWhiteSpace(freeText)) return results;

        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.CompanyId, c.Name, c.EntityType, al.Name, c.ArchiveCode,
       (SELECT en.Name FROM EntityNameHistory en
        WHERE en.EntityId = c.CompanyId AND en.IsCurrent = 0 AND en.Name LIKE $likePattern
        ORDER BY en.FromDate DESC LIMIT 1) AS MatchedPreviousName
FROM Companies c
JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
WHERE c.Name LIKE $likePattern
   OR c.ArchiveCode = $exactText
   OR c.CompanyId IN (SELECT EntityId FROM EntityNameHistory WHERE Name LIKE $likePattern)
LIMIT $max;";
        cmd.Parameters.AddWithValue("$likePattern", $"%{freeText.Trim()}%");
        cmd.Parameters.AddWithValue("$exactText", freeText.Trim());
        cmd.Parameters.AddWithValue("$max", maxResults);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new EntitySearchResult(
                CompanyId: reader.GetInt32(0),
                CurrentName: reader.GetString(1),
                EntityType: reader.GetString(2) == "Person" ? Domain.Models.EntityType.Person : Domain.Models.EntityType.Company,
                StageName: reader.GetString(3),
                ArchiveCode: reader.IsDBNull(4) ? "" : reader.GetString(4),
                MatchedPreviousName: reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return results;
    }
}

public sealed record EntitySearchResult(
    int CompanyId,
    string CurrentName,
    Domain.Models.EntityType EntityType,
    string StageName,
    string ArchiveCode,
    string? MatchedPreviousName);
