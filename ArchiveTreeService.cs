using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.Data;
using ArchiveManager.Infrastructure.FileSystem;
using Microsoft.Data.Sqlite;

namespace ArchiveManager.Application.Services;

public sealed class ArchiveTreeNode
{
    public string DisplayName { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsCompany { get; set; }
    public int? CompanyId { get; set; }
    public int? ArchiveLevelId { get; set; }
    public EntityType EntityType { get; set; } = EntityType.Company;
    public string ArchiveCode { get; set; } = string.Empty;
    public List<ArchiveTreeNode> Children { get; } = new();
}

/// <summary>
/// Builds the left-panel tree from the real archive folders on disk, and keeps
/// the Companies table synchronized (spec §24/§40 — "sync, never silently
/// delete"). This is the read side; company creation/level moves are in
/// DocumentRegistrationService / CompanyManagementService territory.
/// </summary>
public sealed class ArchiveTreeService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SettingsService _settings;
    private readonly FileTypeMapperService _fileTypes;

    public ArchiveTreeService(SqliteConnectionFactory connectionFactory, SettingsService settings, FileTypeMapperService fileTypes)
    {
        _connectionFactory = connectionFactory;
        _settings = settings;
        _fileTypes = fileTypes;
    }

    /// <summary>
    /// Scans ArchiveRoot/<level folders>/<company folders> and returns a tree.
    /// Does not touch the database — call SynchronizeWithDatabase() separately
    /// (kept apart so a plain "browse" never has side effects, per spec §12).
    /// </summary>
    public ArchiveTreeNode BuildTreeFromDisk()
    {
        var root = new ArchiveTreeNode
        {
            DisplayName = "بایگانی شرکت‌ها",
            AbsolutePath = _settings.Current.ArchiveRootPath,
            RelativePath = string.Empty
        };

        if (!_settings.IsArchiveRootConfigured())
        {
            return root; // caller shows the "select archive root" first-run screen
        }

        var entityLookup = LoadEntityLookupByFolderPath();

        foreach (var levelDir in Directory.EnumerateDirectories(root.AbsolutePath).OrderBy(d => d))
        {
            var levelNode = new ArchiveTreeNode
            {
                DisplayName = Path.GetFileName(levelDir),
                AbsolutePath = levelDir,
                RelativePath = Path.GetFileName(levelDir)
            };

            foreach (var companyDir in Directory.EnumerateDirectories(levelDir).OrderBy(d => d))
            {
                var relativePath = Path.GetRelativePath(root.AbsolutePath, companyDir);
                var node = new ArchiveTreeNode
                {
                    DisplayName = Path.GetFileName(companyDir),
                    AbsolutePath = companyDir,
                    RelativePath = relativePath,
                    IsCompany = true
                };
                if (entityLookup.TryGetValue(relativePath, out var entity))
                {
                    node.CompanyId = entity.CompanyId;
                    node.EntityType = entity.EntityType;
                    node.ArchiveCode = entity.ArchiveCode;
                }
                levelNode.Children.Add(node);
            }

            root.Children.Add(levelNode);
        }

        return root;
    }

    private Dictionary<string, (int CompanyId, EntityType EntityType, string ArchiveCode)> LoadEntityLookupByFolderPath()
    {
        var map = new Dictionary<string, (int, EntityType, string)>();
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CompanyId, FolderPath, EntityType, ArchiveCode FROM Companies;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var entityType = !reader.IsDBNull(2) && reader.GetString(2) == "Person" ? EntityType.Person : EntityType.Company;
            map[reader.GetString(1)] = (reader.GetInt32(0), entityType, reader.IsDBNull(3) ? "" : reader.GetString(3));
        }
        return map;
    }

    /// <summary>
    /// Reconciles disk structure with the Companies table: adds newly-discovered
    /// company folders, never deletes a Companies row just because a folder is
    /// temporarily unreadable (spec §40/§9 integrity semantics — that's the job
    /// of IntegrityCheckService, which requires explicit Administrator confirmation).
    /// </summary>
    public void SynchronizeWithDatabase()
    {
        if (!_settings.IsArchiveRootConfigured()) return;

        using var connection = _connectionFactory.CreateOpenConnection();
        var levels = LoadLevels(connection);

        foreach (var levelDir in Directory.EnumerateDirectories(_settings.Current.ArchiveRootPath))
        {
            var levelFolderName = Path.GetFileName(levelDir);
            var level = levels.FirstOrDefault(l => levelFolderName.StartsWith(l.Code, StringComparison.Ordinal));
            if (level is null) continue; // folder doesn't match a known level — left alone, surfaced by integrity check

            foreach (var companyDir in Directory.EnumerateDirectories(levelDir))
            {
                var folderLeafName = Path.GetFileName(companyDir);
                // A folder may carry a numeric ordering prefix like "001-" (addendum
                // "PHYSICAL FOLDER HANDLING" examples: "001-محمد احمدی"). The prefix
                // is a folder-naming convention only — the entity's logical Name (used
                // in the UI, registration panel, and generated filenames) must NOT
                // include it, or every filename would end up reading "... 001-محمد
                // احمدی.pdf" instead of "... محمد احمدی.pdf".
                var companyName = StripNumericFolderPrefix(folderLeafName);
                var relativePath = Path.GetRelativePath(_settings.Current.ArchiveRootPath, companyDir);

                using var check = connection.CreateCommand();
                check.CommandText = "SELECT COUNT(*) FROM Companies WHERE Name = $name AND ArchiveLevelId = $levelId;";
                check.Parameters.AddWithValue("$name", companyName);
                check.Parameters.AddWithValue("$levelId", level.ArchiveLevelId);
                var exists = Convert.ToInt64(check.ExecuteScalar()) > 0;

                if (!exists)
                {
                    // ASSUMPTION (entity-identity addendum): a newly-discovered folder
                    // is assumed to be a Person while it sits in "01 - رشد مقدماتی"
                    // and a Company everywhere else — the operator can correct this
                    // immediately via "تغییر نام"/"انتقال پرونده" if it's wrong.
                    var entityType = level.Code == "01" ? "Person" : "Company";
                    var archiveCode = GenerateNextArchiveCode(connection);
                    var nowIso = DateTime.UtcNow.ToString("O");

                    using var transaction = connection.BeginTransaction();
                    long newId;
                    using (var insert = connection.CreateCommand())
                    {
                        insert.Transaction = transaction;
                        insert.CommandText = @"INSERT INTO Companies (Name, ArchiveLevelId, FolderPath, CreatedAt, EntityType, ArchiveCode, UpdatedAt)
                                                VALUES ($name, $levelId, $path, $created, $entityType, $code, $created);
                                                SELECT last_insert_rowid();";
                        insert.Parameters.AddWithValue("$name", companyName);
                        insert.Parameters.AddWithValue("$levelId", level.ArchiveLevelId);
                        insert.Parameters.AddWithValue("$path", relativePath);
                        insert.Parameters.AddWithValue("$created", nowIso);
                        insert.Parameters.AddWithValue("$entityType", entityType);
                        insert.Parameters.AddWithValue("$code", archiveCode);
                        newId = (long)insert.ExecuteScalar()!;
                    }
                    using (var history = connection.CreateCommand())
                    {
                        history.Transaction = transaction;
                        history.CommandText = @"INSERT INTO EntityNameHistory (EntityId, Name, NameType, FromDate, ToDate, IsCurrent)
                                                 VALUES ($id, $name, $type, $from, NULL, 1);";
                        history.Parameters.AddWithValue("$id", newId);
                        history.Parameters.AddWithValue("$name", companyName);
                        history.Parameters.AddWithValue("$type", entityType);
                        history.Parameters.AddWithValue("$from", nowIso);
                        history.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }
    }

    /// <summary>Strips a leading numeric ordering prefix such as "001-" from a
    /// folder leaf name, leaving the entity's logical name (e.g. "001-محمد
    /// احمدی" → "محمد احمدی"). Mirrors the prefix-preserving logic in
    /// EntityTransitionService.BuildFolderLeafName, which does the reverse
    /// (re-attaches the same prefix when building a new folder name).</summary>
    internal static string StripNumericFolderPrefix(string folderLeafName)
    {
        var dashIndex = folderLeafName.IndexOf('-');
        if (dashIndex > 0 && folderLeafName[..dashIndex].All(char.IsDigit))
        {
            return folderLeafName[(dashIndex + 1)..].Trim();
        }
        return folderLeafName;
    }

    /// <summary>Same stable "A-000N" code generation used by EntityTransitionService
    /// at entity creation — duplicated as a tiny static-free query here (rather than
    /// taking a dependency on EntityTransitionService) to avoid a circular reference,
    /// since both classes only need read access to the Companies table for this.</summary>
    private static string GenerateNextArchiveCode(SqliteConnection connection)
    {
        var next = 1;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ArchiveCode FROM Companies WHERE ArchiveCode LIKE 'A-%';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var code = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (code is { Length: > 2 } && int.TryParse(code[2..], out var n) && n >= next)
            {
                next = n + 1;
            }
        }
        return $"A-{next:D4}";
    }

    private static List<ArchiveLevel> LoadLevels(SqliteConnection connection)
    {
        var levels = new List<ArchiveLevel>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ArchiveLevelId, Code, Name, SortOrder FROM ArchiveLevels ORDER BY SortOrder;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            levels.Add(new ArchiveLevel
            {
                ArchiveLevelId = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                SortOrder = reader.GetInt32(3)
            });
        }
        return levels;
    }
}
