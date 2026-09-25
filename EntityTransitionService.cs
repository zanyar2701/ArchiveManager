using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.Data;
using Microsoft.Data.Sqlite;

namespace ArchiveManager.Application.Services;

public sealed record StageTransferRequest(
    int EntityId,
    int ToStageId,
    EntityType NewEntityType,
    string NewName,
    string? Reason,
    int? OperatorId,
    bool MoveHistoricalFilenamesToo = false); // never defaults to true — addendum: "must NEVER happen automatically"

public sealed record RenameEntityRequest(
    int EntityId,
    string NewName,
    string? Reason,
    int? OperatorId,
    bool RenamePhysicalFolder = true);

public sealed class TransitionResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? NewAbsoluteFolderPath { get; init; }
    public string? NewRelativeFolderPath { get; init; }
}

/// <summary>
/// Implements "تغییر مرحله / انتقال پرونده" and "تغییر نام" — the entity-identity
/// workflow from the addendum. The one hard invariant enforced everywhere here:
/// the physical folder move (if any) happens FIRST and must fully succeed before
/// any database row is written; if the move fails, nothing is touched (addendum:
/// "If the physical folder cannot be moved, DO NOT modify the database as if the
/// operation succeeded."). Companies.CompanyId is never touched — it IS the
/// stable internal entity ID the addendum asks for, so history always resolves
/// back to the same record regardless of name/type/stage changes.
/// </summary>
public sealed class EntityTransitionService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SettingsService _settings;
    private readonly ActivityLogService _activityLog;

    public EntityTransitionService(SqliteConnectionFactory connectionFactory, SettingsService settings, ActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _settings = settings;
        _activityLog = activityLog;
    }

    /// <summary>"انتقال پرونده" — moves an entity between archive levels, optionally
    /// changing its identity type and name at the same time (e.g. Person → Company).</summary>
    public TransitionResult TransferStage(StageTransferRequest request)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        var current = LoadCompany(connection, request.EntityId);
        if (current is null)
        {
            return new TransitionResult { Succeeded = false, ErrorMessage = "پرونده مورد نظر یافت نشد." };
        }

        var targetLevel = LoadLevel(connection, request.ToStageId);
        if (targetLevel is null)
        {
            return new TransitionResult { Succeeded = false, ErrorMessage = "مرحله مقصد نامعتبر است." };
        }

        var oldLevel = LoadLevel(connection, current.ArchiveLevelId);
        var archiveRoot = _settings.Current.ArchiveRootPath;
        var oldAbsolutePath = Path.Combine(archiveRoot, current.FolderPath);

        // Build the new folder path: same numeric prefix if the folder used one
        // (e.g. "001-"), new level folder, new display name.
        var folderLeaf = BuildFolderLeafName(current.FolderPath, request.NewName);
        var newRelativePath = Path.Combine(targetLevel.FolderName, folderLeaf);
        var newAbsolutePath = Path.Combine(archiveRoot, newRelativePath);

        // --- Physical move FIRST. Never touch the DB unless this fully succeeds. ---
        try
        {
            if (!Directory.Exists(oldAbsolutePath))
            {
                return new TransitionResult { Succeeded = false, ErrorMessage = $"پوشه پرونده در مسیر مورد انتظار یافت نشد: {oldAbsolutePath}" };
            }
            if (Directory.Exists(newAbsolutePath))
            {
                return new TransitionResult { Succeeded = false, ErrorMessage = "پوشه‌ای با این نام در مرحله مقصد از قبل وجود دارد." };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(newAbsolutePath)!);
            Directory.Move(oldAbsolutePath, newAbsolutePath); // atomic on the same volume
        }
        catch (IOException ex)
        {
            return new TransitionResult { Succeeded = false, ErrorMessage = $"امکان جابه‌جایی پوشه پرونده وجود ندارد: {ex.Message}" };
        }
        catch (UnauthorizedAccessException)
        {
            return new TransitionResult { Succeeded = false, ErrorMessage = "دسترسی لازم برای جابه‌جایی پوشه پرونده وجود ندارد." };
        }

        // --- Folder move succeeded — now commit the database side, transactionally. ---
        try
        {
            using var transaction = connection.BeginTransaction();
            var nowIso = DateTime.UtcNow.ToString("O");

            // خروج‌یافته preserves the prior stage, per spec §25/addendum "EXITED ARCHIVE".
            var isExiting = targetLevel.Code == "04";
            var previousLevelIdToStore = isExiting ? current.ArchiveLevelId : (int?)null;

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"UPDATE Companies SET
                    Name = $name, ArchiveLevelId = $levelId, FolderPath = $folderPath,
                    PreviousLevelId = $prevLevelId, EntityType = $entityType, UpdatedAt = $updated
                    WHERE CompanyId = $id;";
                update.Parameters.AddWithValue("$name", request.NewName);
                update.Parameters.AddWithValue("$levelId", request.ToStageId);
                update.Parameters.AddWithValue("$folderPath", newRelativePath);
                update.Parameters.AddWithValue("$prevLevelId", (object?)previousLevelIdToStore ?? DBNull.Value);
                update.Parameters.AddWithValue("$entityType", request.NewEntityType.ToString());
                update.Parameters.AddWithValue("$updated", nowIso);
                update.Parameters.AddWithValue("$id", request.EntityId);
                update.ExecuteNonQuery();
            }

            CloseCurrentNameHistory(connection, transaction, request.EntityId, nowIso);
            InsertNameHistory(connection, transaction, request.EntityId, request.NewName, request.NewEntityType, nowIso, isCurrent: true);

            using (var insertTransition = connection.CreateCommand())
            {
                insertTransition.Transaction = transaction;
                insertTransition.CommandText = @"INSERT INTO EntityTransitions
                    (EntityId, FromStageId, ToStageId, FromEntityType, ToEntityType, PreviousName, NewName, Date, Reason, OperatorId)
                    VALUES ($entityId, $fromStage, $toStage, $fromType, $toType, $prevName, $newName, $date, $reason, $operatorId);";
                insertTransition.Parameters.AddWithValue("$entityId", request.EntityId);
                insertTransition.Parameters.AddWithValue("$fromStage", current.ArchiveLevelId);
                insertTransition.Parameters.AddWithValue("$toStage", request.ToStageId);
                insertTransition.Parameters.AddWithValue("$fromType", current.EntityType.ToString());
                insertTransition.Parameters.AddWithValue("$toType", request.NewEntityType.ToString());
                insertTransition.Parameters.AddWithValue("$prevName", current.Name);
                insertTransition.Parameters.AddWithValue("$newName", request.NewName);
                insertTransition.Parameters.AddWithValue("$date", nowIso);
                insertTransition.Parameters.AddWithValue("$reason", (object?)request.Reason ?? DBNull.Value);
                insertTransition.Parameters.AddWithValue("$operatorId", (object?)request.OperatorId ?? DBNull.Value);
                insertTransition.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            // The DB write failed AFTER the folder was already moved — move it
            // back so disk and database never disagree about where the entity is.
            try { Directory.Move(newAbsolutePath, oldAbsolutePath); } catch { /* best effort */ }
            return new TransitionResult { Succeeded = false, ErrorMessage = $"عملیات ناموفق بود و پوشه به مسیر قبلی بازگردانده شد: {ex.Message}" };
        }

        _activityLog.Log("انتقال پرونده", null,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                entityId = request.EntityId,
                from = $"{oldLevel?.Name}/{current.Name}",
                to = $"{targetLevel.Name}/{request.NewName}",
                reason = request.Reason
            }),
            request.OperatorId);

        return new TransitionResult { Succeeded = true, NewAbsoluteFolderPath = newAbsolutePath, NewRelativeFolderPath = newRelativePath };
    }

    /// <summary>"تغییر نام" — renames an entity without changing its stage.</summary>
    public TransitionResult RenameEntity(RenameEntityRequest request)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        var current = LoadCompany(connection, request.EntityId);
        if (current is null)
        {
            return new TransitionResult { Succeeded = false, ErrorMessage = "پرونده مورد نظر یافت نشد." };
        }

        var archiveRoot = _settings.Current.ArchiveRootPath;
        var oldAbsolutePath = Path.Combine(archiveRoot, current.FolderPath);
        var newRelativePath = current.FolderPath;
        var newAbsolutePath = oldAbsolutePath;

        if (request.RenamePhysicalFolder)
        {
            var folderLeaf = BuildFolderLeafName(current.FolderPath, request.NewName);
            var levelFolder = Path.GetDirectoryName(current.FolderPath) ?? string.Empty;
            newRelativePath = Path.Combine(levelFolder, folderLeaf);
            newAbsolutePath = Path.Combine(archiveRoot, newRelativePath);

            try
            {
                if (!Directory.Exists(oldAbsolutePath))
                {
                    return new TransitionResult { Succeeded = false, ErrorMessage = $"پوشه پرونده یافت نشد: {oldAbsolutePath}" };
                }
                if (!string.Equals(oldAbsolutePath, newAbsolutePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(newAbsolutePath))
                    {
                        return new TransitionResult { Succeeded = false, ErrorMessage = "پوشه‌ای با این نام از قبل وجود دارد." };
                    }
                    Directory.Move(oldAbsolutePath, newAbsolutePath);
                }
            }
            catch (IOException ex)
            {
                return new TransitionResult { Succeeded = false, ErrorMessage = $"امکان تغییرنام پوشه وجود ندارد: {ex.Message}" };
            }
        }

        try
        {
            using var transaction = connection.BeginTransaction();
            var nowIso = DateTime.UtcNow.ToString("O");

            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE Companies SET Name = $name, FolderPath = $folderPath, UpdatedAt = $updated WHERE CompanyId = $id;";
                update.Parameters.AddWithValue("$name", request.NewName);
                update.Parameters.AddWithValue("$folderPath", newRelativePath);
                update.Parameters.AddWithValue("$updated", nowIso);
                update.Parameters.AddWithValue("$id", request.EntityId);
                update.ExecuteNonQuery();
            }

            CloseCurrentNameHistory(connection, transaction, request.EntityId, nowIso);
            InsertNameHistory(connection, transaction, request.EntityId, request.NewName, current.EntityType, nowIso, isCurrent: true);

            using (var insertTransition = connection.CreateCommand())
            {
                insertTransition.Transaction = transaction;
                insertTransition.CommandText = @"INSERT INTO EntityTransitions
                    (EntityId, FromStageId, ToStageId, FromEntityType, ToEntityType, PreviousName, NewName, Date, Reason, OperatorId)
                    VALUES ($entityId, $stage, $stage, $type, $type, $prevName, $newName, $date, $reason, $operatorId);";
                insertTransition.Parameters.AddWithValue("$entityId", request.EntityId);
                insertTransition.Parameters.AddWithValue("$stage", current.ArchiveLevelId);
                insertTransition.Parameters.AddWithValue("$type", current.EntityType.ToString());
                insertTransition.Parameters.AddWithValue("$prevName", current.Name);
                insertTransition.Parameters.AddWithValue("$newName", request.NewName);
                insertTransition.Parameters.AddWithValue("$date", nowIso);
                insertTransition.Parameters.AddWithValue("$reason", (object?)request.Reason ?? DBNull.Value);
                insertTransition.Parameters.AddWithValue("$operatorId", (object?)request.OperatorId ?? DBNull.Value);
                insertTransition.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            if (request.RenamePhysicalFolder && !string.Equals(oldAbsolutePath, newAbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Move(newAbsolutePath, oldAbsolutePath); } catch { /* best effort */ }
            }
            return new TransitionResult { Succeeded = false, ErrorMessage = $"عملیات ناموفق بود: {ex.Message}" };
        }

        _activityLog.Log("تغییر نام", null,
            System.Text.Json.JsonSerializer.Serialize(new { entityId = request.EntityId, from = current.Name, to = request.NewName, reason = request.Reason }),
            request.OperatorId);

        // Historical documents are intentionally NOT renamed here (addendum:
        // "This must NEVER happen automatically") — a separate, explicit,
        // operator-triggered bulk-rename action would call DocumentRegistrationService
        // per file if the operator opts into "به‌روزرسانی نام فایل‌های قدیمی".

        return new TransitionResult { Succeeded = true, NewAbsoluteFolderPath = newAbsolutePath, NewRelativeFolderPath = newRelativePath };
    }

    /// <summary>"تاریخچه پرونده" — merges creation + transitions into one chronological timeline.</summary>
    public List<EntityHistoryEvent> GetHistory(int entityId)
    {
        var events = new List<EntityHistoryEvent>();
        using var connection = _connectionFactory.CreateOpenConnection();

        using (var creationCmd = connection.CreateCommand())
        {
            creationCmd.CommandText = @"
SELECT c.CreatedAt, en.Name, al.Name
FROM Companies c
JOIN ArchiveLevels al ON al.ArchiveLevelId = c.ArchiveLevelId
LEFT JOIN EntityNameHistory en ON en.EntityId = c.CompanyId AND en.FromDate = c.CreatedAt
WHERE c.CompanyId = $id;";
            creationCmd.Parameters.AddWithValue("$id", entityId);
            using var reader = creationCmd.ExecuteReader();
            if (reader.Read())
            {
                var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                events.Add(new EntityHistoryEvent(DateTime.Parse(reader.GetString(0)), "ایجاد پرونده", $"{name} — {reader.GetString(2)}"));
            }
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT Date, FromStageId, ToStageId, FromEntityType, ToEntityType, PreviousName, NewName, Reason
                                 FROM EntityTransitions WHERE EntityId = $id ORDER BY Date;";
            cmd.Parameters.AddWithValue("$id", entityId);
            using var reader = cmd.ExecuteReader();
            var levels = LoadAllLevels(connection);
            while (reader.Read())
            {
                var date = DateTime.Parse(reader.GetString(0));
                var fromStageId = reader.GetInt32(1);
                var toStageId = reader.GetInt32(2);
                var fromType = reader.GetString(3);
                var toType = reader.GetString(4);
                var prevName = reader.GetString(5);
                var newName = reader.GetString(6);
                var reason = reader.IsDBNull(7) ? null : reader.GetString(7);

                if (fromStageId != toStageId)
                {
                    var fromName = levels.TryGetValue(fromStageId, out var fl) ? fl : "?";
                    var toName = levels.TryGetValue(toStageId, out var tl) ? tl : "?";
                    events.Add(new EntityHistoryEvent(date, "انتقال مرحله", $"{fromName} → {toName}"));
                }
                if (fromType != toType)
                {
                    var fa = fromType == "Person" ? "شخص" : "شرکت";
                    var ta = toType == "Person" ? "شخص" : "شرکت";
                    events.Add(new EntityHistoryEvent(date, "تغییر هویت", $"{fa} → {ta}"));
                }
                if (prevName != newName)
                {
                    events.Add(new EntityHistoryEvent(date, "تغییر نام", $"{prevName} → {newName}"));
                }
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    events.Add(new EntityHistoryEvent(date, "توضیحات", reason!));
                }
            }
        }

        return events.OrderBy(e => e.Date).ToList();
    }

    /// <summary>Generates the next free stable "A-000N" archive code (addendum:
    /// "This code must NEVER change"). Called once, at entity creation.</summary>
    public string GenerateNextArchiveCode(SqliteConnection connection)
    {
        var next = 1;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ArchiveCode FROM Companies WHERE ArchiveCode LIKE 'A-%';";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var code = reader.GetString(0);
            if (code.Length > 2 && int.TryParse(code[2..], out var n) && n >= next)
            {
                next = n + 1;
            }
        }
        return $"A-{next:D4}";
    }

    // --------------------------- helpers ---------------------------

    private static Company? LoadCompany(SqliteConnection connection, int id)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT CompanyId, Name, ArchiveLevelId, FolderPath, PreviousLevelId, CreatedAt,
                                    EntityType, ArchiveCode, UpdatedAt
                             FROM Companies WHERE CompanyId = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new Company
        {
            CompanyId = reader.GetInt32(0),
            Name = reader.GetString(1),
            ArchiveLevelId = reader.GetInt32(2),
            FolderPath = reader.GetString(3),
            PreviousLevelId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            CreatedAt = DateTime.Parse(reader.GetString(5)),
            EntityType = reader.IsDBNull(6) || reader.GetString(6) == "Person" ? EntityType.Person : EntityType.Company,
            ArchiveCode = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            UpdatedAt = reader.IsDBNull(8) ? reader.GetDateTime(5) : DateTime.Parse(reader.GetString(8))
        };
    }

    private static ArchiveLevel? LoadLevel(SqliteConnection connection, int id)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ArchiveLevelId, Code, Name, SortOrder FROM ArchiveLevels WHERE ArchiveLevelId = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ArchiveLevel { ArchiveLevelId = reader.GetInt32(0), Code = reader.GetString(1), Name = reader.GetString(2), SortOrder = reader.GetInt32(3) };
    }

    private static Dictionary<int, string> LoadAllLevels(SqliteConnection connection)
    {
        var result = new Dictionary<int, string>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ArchiveLevelId, Name FROM ArchiveLevels;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) result[reader.GetInt32(0)] = reader.GetString(1);
        return result;
    }

    private static void CloseCurrentNameHistory(SqliteConnection connection, SqliteTransaction transaction, int entityId, string nowIso)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE EntityNameHistory SET IsCurrent = 0, ToDate = $now WHERE EntityId = $id AND IsCurrent = 1;";
        cmd.Parameters.AddWithValue("$now", nowIso);
        cmd.Parameters.AddWithValue("$id", entityId);
        cmd.ExecuteNonQuery();
    }

    private static void InsertNameHistory(SqliteConnection connection, SqliteTransaction transaction, int entityId, string name, EntityType type, string fromIso, bool isCurrent)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"INSERT INTO EntityNameHistory (EntityId, Name, NameType, FromDate, ToDate, IsCurrent)
                             VALUES ($id, $name, $type, $from, NULL, $current);";
        cmd.Parameters.AddWithValue("$id", entityId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.Parameters.AddWithValue("$from", fromIso);
        cmd.Parameters.AddWithValue("$current", isCurrent ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Preserves a numeric folder prefix if the existing folder used one
    /// (e.g. "001-محمد احمدی" → "001-شرکت فناوران کردستان"); otherwise just uses
    /// the new name as-is. Also strips Windows-invalid characters.</summary>
    private static string BuildFolderLeafName(string currentRelativeFolderPath, string newName)
    {
        var currentLeaf = Path.GetFileName(currentRelativeFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dashIndex = currentLeaf.IndexOf('-');
        var prefix = string.Empty;
        if (dashIndex > 0 && currentLeaf[..dashIndex].All(char.IsDigit))
        {
            prefix = currentLeaf[..(dashIndex + 1)];
        }

        var sanitized = newName;
        foreach (var invalid in new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
        {
            sanitized = sanitized.Replace(invalid, ' ');
        }
        return prefix + sanitized.Trim();
    }
}
