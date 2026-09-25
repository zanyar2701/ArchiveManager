using System;
using System.Collections.Generic;
using System.IO;
using ArchiveManager.Infrastructure.Data;

namespace ArchiveManager.Application.Services;

public sealed class IntegrityReport
{
    public int FilesChecked { get; set; }
    public int HealthyFiles { get; set; }
    public List<string> MissingFiles { get; set; } = new();     // in DB, not on disk
    public List<string> InvalidPaths { get; set; } = new();
    public List<string> AccessErrors { get; set; } = new();
    public List<string> UnregisteredFiles { get; set; } = new(); // on disk, not in DB
}

/// <summary>
/// "بررسی سلامت بایگانی" (spec §22). Read-only: flags issues, never deletes a
/// DB record on its own — an Administrator must confirm removal (edge case #9/#16).
/// </summary>
public sealed class IntegrityCheckService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SettingsService _settings;
    private readonly ActivityLogService _activityLog;

    public IntegrityCheckService(SqliteConnectionFactory connectionFactory, SettingsService settings, ActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _settings = settings;
        _activityLog = activityLog;
    }

    public IntegrityReport Run()
    {
        var report = new IntegrityReport();
        if (!_settings.IsArchiveRootConfigured())
        {
            report.InvalidPaths.Add(_settings.Current.ArchiveRootPath);
            return report;
        }

        using var connection = _connectionFactory.CreateOpenConnection();
        var knownRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT DocumentId, RelativePath FROM Documents;";
            using var reader = cmd.ExecuteReader();
            var toMarkMissing = new List<int>();
            while (reader.Read())
            {
                report.FilesChecked++;
                var docId = reader.GetInt32(0);
                var relPath = reader.GetString(1);
                knownRelativePaths.Add(relPath);
                var absolutePath = Path.Combine(_settings.Current.ArchiveRootPath, relPath);

                try
                {
                    if (File.Exists(absolutePath))
                    {
                        report.HealthyFiles++;
                    }
                    else
                    {
                        report.MissingFiles.Add(relPath);
                        toMarkMissing.Add(docId);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    report.AccessErrors.Add(relPath);
                }
                catch (IOException)
                {
                    report.InvalidPaths.Add(relPath);
                }
            }

            foreach (var docId in toMarkMissing)
            {
                using var update = connection.CreateCommand();
                update.CommandText = "UPDATE Documents SET IsMissing = 1 WHERE DocumentId = $id;";
                update.Parameters.AddWithValue("$id", docId);
                update.ExecuteNonQuery();
            }
        }

        // Files on disk with no matching DB record ("ثبت‌نشده" — spec §5).
        foreach (var levelDir in Directory.EnumerateDirectories(_settings.Current.ArchiveRootPath))
        {
            foreach (var companyDir in Directory.EnumerateDirectories(levelDir))
            {
                foreach (var file in Directory.EnumerateFiles(companyDir))
                {
                    var relPath = Path.GetRelativePath(_settings.Current.ArchiveRootPath, file);
                    if (!knownRelativePaths.Contains(relPath))
                    {
                        report.UnregisteredFiles.Add(relPath);
                    }
                }
            }
        }

        _activityLog.Log("بررسی سلامت", null,
            $"{{\"checked\":{report.FilesChecked},\"missing\":{report.MissingFiles.Count},\"unregistered\":{report.UnregisteredFiles.Count}}}",
            null);

        return report;
    }
}
