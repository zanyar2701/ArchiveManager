using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchiveManager.Application.Services;

public sealed class AppSettings
{
    public string ArchiveRootPath { get; set; } = string.Empty;
    public string Language { get; set; } = "fa"; // "fa" | "en" — spec §14
    public bool UsePersianDigits { get; set; } = true;
    public FileTransferPolicy TransferPolicy { get; set; } = FileTransferPolicy.Copy;
    public int BackupRetentionCount { get; set; } = 30;
    public bool TreeExpandedStatePersisted { get; set; } = true;
}

public enum FileTransferPolicy { Copy, Move }

/// <summary>
/// Loads/saves Config/settings.json. First run: seeded from
/// Config/settings.default.json, then the user is prompted to pick the
/// archive root if it's still empty (spec §19/§23, edge case #21/#23).
/// </summary>
public sealed class SettingsService
{
    private readonly string _configPath;
    public AppSettings Current { get; private set; }

    private SettingsService(string configPath, AppSettings settings)
    {
        _configPath = configPath;
        Current = settings;
    }

    public static SettingsService LoadOrCreate(string configPath, string defaultConfigPath)
    {
        AppSettings settings;
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        else if (File.Exists(defaultConfigPath))
        {
            var json = File.ReadAllText(defaultConfigPath);
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        else
        {
            settings = new AppSettings();
        }

        var service = new SettingsService(configPath, settings);
        service.Save(); // persist whatever we ended up with, so Config/settings.json always exists
        return service;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    public void UpdateArchiveRoot(string path)
    {
        Current.ArchiveRootPath = path;
        Save();
    }

    public bool IsArchiveRootConfigured() =>
        !string.IsNullOrWhiteSpace(Current.ArchiveRootPath) && Directory.Exists(Current.ArchiveRootPath);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
