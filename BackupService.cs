using System;
using System.IO;
using System.Linq;
using ArchiveManager.Infrastructure.Data;

namespace ArchiveManager.Application.Services;

/// <summary>
/// Backs up the SQLite database (+ config) to Backup/, never the archive
/// documents themselves (spec §19-build: "do not automatically duplicate
/// the entire document archive"). Rotates old backups per BackupRetentionCount.
/// </summary>
public sealed class BackupService
{
    private readonly string _appRoot;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly SettingsService _settings;
    private readonly ActivityLogService _activityLog;

    public BackupService(string appRoot, SqliteConnectionFactory connectionFactory, SettingsService settings, ActivityLogService activityLog)
    {
        _appRoot = appRoot;
        _connectionFactory = connectionFactory;
        _settings = settings;
        _activityLog = activityLog;
    }

    public string CreateBackup()
    {
        var backupDir = Path.Combine(_appRoot, "Backup");
        Directory.CreateDirectory(backupDir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var backupDbPath = Path.Combine(backupDir, $"archive_{timestamp}.db");

        // Use SQLite's own backup API via VACUUM INTO — a safe, consistent
        // point-in-time copy even while the app has the DB open (WAL mode).
        using var connection = _connectionFactory.CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path;";
        cmd.Parameters.AddWithValue("$path", backupDbPath);
        cmd.ExecuteNonQuery();

        var configSource = Path.Combine(_appRoot, "Config", "settings.json");
        var configBackup = Path.Combine(backupDir, $"settings_{timestamp}.json");
        if (File.Exists(configSource))
        {
            File.Copy(configSource, configBackup, overwrite: true);
        }

        RotateOldBackups(backupDir);
        _activityLog.Log("پشتیبان‌گیری", null, $"{{\"file\":\"{Path.GetFileName(backupDbPath)}\"}}", null);

        return backupDbPath;
    }

    /// <summary>Restores from a chosen backup file, after itself backing up the
    /// CURRENT state first — a safety net per spec edge case #30.</summary>
    public void RestoreBackup(string backupDbPath)
    {
        CreateBackup(); // safety net: never restore without a pre-restore snapshot

        var liveDbPath = Path.Combine(_appRoot, "Data", "archive.db");
        File.Copy(backupDbPath, liveDbPath, overwrite: true);
        // WAL/SHM sidecar files, if any, are stale after a raw file copy —
        // remove them so SQLite rebuilds them cleanly on next open.
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = liveDbPath + suffix;
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }

        _activityLog.Log("بازیابی پایگاه داده", null, $"{{\"from\":\"{Path.GetFileName(backupDbPath)}\"}}", null);
    }

    private void RotateOldBackups(string backupDir)
    {
        var retain = Math.Max(1, _settings.Current.BackupRetentionCount);
        var dbBackups = Directory.GetFiles(backupDir, "archive_*.db")
            .OrderByDescending(f => f)
            .Skip(retain);
        foreach (var old in dbBackups)
        {
            try { File.Delete(old); } catch { /* best effort */ }
        }
    }
}
