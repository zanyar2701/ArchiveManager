using System.Collections.Generic;
using ArchiveManager.Infrastructure.Data;

namespace ArchiveManager.Application.Services;

public sealed record NameCount(string Name, int Count);

public sealed class ArchiveStatistics
{
    public int CompanyCount { get; set; }
    public int DocumentCount { get; set; }
    public List<NameCount> ByYear { get; set; } = new();
    public List<NameCount> ByDocumentType { get; set; } = new();
    public List<NameCount> ByCompany { get; set; } = new();
    public List<NameCount> ByArchiveLevel { get; set; } = new();
    public List<NameCount> ByFormat { get; set; } = new();
    public int MissingFileCount { get; set; }

    // --- Entity-identity reports (addendum) ---
    public List<NameCount> EntitiesByStage { get; set; } = new();
    public int PeopleInPreGrowthCount { get; set; }
    public int CompaniesInGrowthCount { get; set; }
    public int CompaniesInParkCount { get; set; }
    public int ExitedEntitiesCount { get; set; }
    public List<EntityTransitionSummary> RecentTransfers { get; set; } = new();
    public List<EntityTransitionSummary> RecentNameChanges { get; set; } = new();
    public List<EntityTransitionSummary> PersonToCompanyTransitions { get; set; } = new();
    public int EntitiesWithPreviousNamesCount { get; set; }
    public int EntitiesWithIncompleteCompanyInfoCount { get; set; }
}

public sealed record EntityTransitionSummary(string EntityName, string Description, DateTime Date);

/// <summary>One row of the "COMPANY LIST" screen (entity-identity addendum):
/// کد پرونده | نام فعلی | نوع | مرحله | نام قبلی | تاریخ ورود | آخرین تغییر | وضعیت.</summary>
public sealed record EntityListRow(
    string ArchiveCode, string CurrentName, string EntityTypeDisplay, string StageName,
    string? PreviousName, DateTime EnteredAt, DateTime LastUpdatedAt, string StatusDisplay);

/// <summary>Read-only aggregate queries for the Reports screen (spec §11).
/// Never mutates anything — enforced simply by only ever executing SELECTs.</summary>
public sealed class StatisticsService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public StatisticsService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public ArchiveStatistics ComputeStatistics()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        var stats = new ArchiveStatistics();

        stats.CompanyCount = Scalar(connection, "SELECT COUNT(*) FROM Companies;");
        stats.DocumentCount = Scalar(connection, "SELECT COUNT(*) FROM Documents;");
        stats.MissingFileCount = Scalar(connection, "SELECT COUNT(*) FROM Documents WHERE IsMissing = 1;");

        stats.ByYear = NameCounts(connection,
            "SELECT CAST(Year AS TEXT), COUNT(*) FROM Documents GROUP BY Year ORDER BY Year DESC;");
        stats.ByDocumentType = NameCounts(connection,
            @"SELECT t.Name, COUNT(*) FROM Documents d JOIN DocumentTypes t ON t.DocumentTypeId = d.DocumentTypeId
              GROUP BY t.Name ORDER BY COUNT(*) DESC;");
        stats.ByCompany = NameCounts(connection,
            @"SELECT c.Name, COUNT(*) FROM Documents d JOIN Companies c ON c.CompanyId = d.CompanyId
              GROUP BY c.Name ORDER BY COUNT(*) DESC LIMIT 20;");
        stats.ByArchiveLevel = NameCounts(connection,
            @"SELECT al.Name, COUNT(*) FROM Documents d
              JOIN Companies c ON c.CompanyId = d.CompanyId
              JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
              GROUP BY al.Name ORDER BY al.SortOrder;");
        stats.ByFormat = NameCounts(connection,
            @"SELECT f.Label, COUNT(*) FROM Documents d JOIN FileFormats f ON f.FileFormatId = d.FileFormatId
              GROUP BY f.Label ORDER BY COUNT(*) DESC;");

        // --- Entity-identity reports (addendum "REPORTING") ---
        stats.EntitiesByStage = NameCounts(connection,
            @"SELECT al.Name, COUNT(*) FROM Companies c JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
              GROUP BY al.Name ORDER BY al.SortOrder;");

        stats.PeopleInPreGrowthCount = Scalar(connection,
            @"SELECT COUNT(*) FROM Companies c JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
              WHERE al.Code = '01' AND c.EntityType = 'Person';");
        stats.CompaniesInGrowthCount = Scalar(connection,
            @"SELECT COUNT(*) FROM Companies c JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
              WHERE al.Code = '02' AND c.EntityType = 'Company';");
        stats.CompaniesInParkCount = Scalar(connection,
            @"SELECT COUNT(*) FROM Companies c JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
              WHERE al.Code = '03' AND c.EntityType = 'Company';");
        stats.ExitedEntitiesCount = Scalar(connection,
            @"SELECT COUNT(*) FROM Companies c JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
              WHERE al.Code = '04';");

        stats.RecentTransfers = TransitionSummaries(connection,
            @"SELECT c.Name, al1.Name, al2.Name, t.Date FROM EntityTransitions t
              JOIN Companies c ON c.CompanyId = t.EntityId
              JOIN ArchiveLevels al1 ON al1.ArchiveLevelId = t.FromStageId
              JOIN ArchiveLevels al2 ON al2.ArchiveLevelId = t.ToStageId
              WHERE t.FromStageId <> t.ToStageId
              ORDER BY t.Date DESC LIMIT 15;", isStageTransfer: true);

        stats.RecentNameChanges = TransitionSummaries(connection,
            @"SELECT c.Name, t.PreviousName, t.NewName, t.Date FROM EntityTransitions t
              JOIN Companies c ON c.CompanyId = t.EntityId
              WHERE t.PreviousName <> t.NewName
              ORDER BY t.Date DESC LIMIT 15;", isStageTransfer: false);

        stats.PersonToCompanyTransitions = TransitionSummaries(connection,
            @"SELECT c.Name, t.PreviousName, t.NewName, t.Date FROM EntityTransitions t
              JOIN Companies c ON c.CompanyId = t.EntityId
              WHERE t.FromEntityType = 'Person' AND t.ToEntityType = 'Company'
              ORDER BY t.Date DESC LIMIT 15;", isStageTransfer: false);

        stats.EntitiesWithPreviousNamesCount = Scalar(connection,
            @"SELECT COUNT(DISTINCT EntityId) FROM EntityNameHistory WHERE IsCurrent = 0;");

        stats.EntitiesWithIncompleteCompanyInfoCount = Scalar(connection,
            @"SELECT COUNT(*) FROM Companies c
              WHERE c.EntityType = 'Company' AND (
                  c.CompanyId NOT IN (SELECT EntityId FROM CompanyProfiles)
                  OR c.CompanyId IN (
                      SELECT EntityId FROM CompanyProfiles
                      WHERE NationalId IS NULL OR RegistrationNumber IS NULL OR RegistrationDate IS NULL
                  )
              );");

        return stats;
    }

    private static List<EntityTransitionSummary> TransitionSummaries(Microsoft.Data.Sqlite.SqliteConnection connection, string sql, bool isStageTransfer)
    {
        var list = new List<EntityTransitionSummary>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var entityName = reader.GetString(0);
            var fromText = reader.GetString(1);
            var toText = reader.GetString(2);
            var date = System.DateTime.Parse(reader.GetString(3));
            var description = isStageTransfer ? $"{fromText} → {toText}" : $"{fromText} → {toText}";
            list.Add(new EntityTransitionSummary(entityName, description, date));
        }
        return list;
    }

    /// <summary>Backs the "COMPANY LIST" table (entity-identity addendum). Shows every
    /// archive entity with its stable code, current identity, and most recent previous
    /// name — e.g. "A-0001 | شرکت فناوران کردستان | شرکت | رشد | محمد احمدی | ...".</summary>
    public List<EntityListRow> ListEntities()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        var rows = new List<EntityListRow>();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT c.ArchiveCode, c.Name, c.EntityType, al.Name, c.CreatedAt, c.UpdatedAt,
       (SELECT en.Name FROM EntityNameHistory en
        WHERE en.EntityId = c.CompanyId AND en.IsCurrent = 0
        ORDER BY en.FromDate DESC LIMIT 1) AS PreviousName
FROM Companies c
JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
ORDER BY c.ArchiveCode;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var entityType = reader.GetString(2) == "Person" ? "شخص" : "شرکت";
            var stageName = reader.GetString(3);
            var status = stageName.Contains("خروج") ? "خروج‌یافته" : "فعال";
            rows.Add(new EntityListRow(
                ArchiveCode: reader.IsDBNull(0) ? "" : reader.GetString(0),
                CurrentName: reader.GetString(1),
                EntityTypeDisplay: entityType,
                StageName: stageName,
                PreviousName: reader.IsDBNull(6) ? null : reader.GetString(6),
                EnteredAt: DateTime.Parse(reader.GetString(4)),
                LastUpdatedAt: reader.IsDBNull(5) ? DateTime.Parse(reader.GetString(4)) : DateTime.Parse(reader.GetString(5)),
                StatusDisplay: status));
        }
        return rows;
    }

    public List<(string FileName, int Count)> FindDuplicateFileNames()
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FileName, COUNT(*) c FROM Documents GROUP BY FileName HAVING c > 1;";
        using var reader = cmd.ExecuteReader();
        var results = new List<(string, int)>();
        while (reader.Read()) results.Add((reader.GetString(0), reader.GetInt32(1)));
        return results;
    }

    private static int Scalar(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return System.Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static List<NameCount> NameCounts(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var list = new List<NameCount>();
        while (reader.Read())
        {
            list.Add(new NameCount(reader.GetString(0), reader.GetInt32(1)));
        }
        return list;
    }
}
