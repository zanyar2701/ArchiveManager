using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ArchiveManager.Application.Services;

/// <summary>
/// Loads Localization/strings.<lang>.json and exposes lookups by key.
/// A JSON dictionary was chosen over .resx (spec's own suggestion) because it
/// keeps translations editable by a non-developer without Visual Studio, and
/// it is trivially reloadable at runtime when the user flips FA|EN (spec §14).
/// </summary>
public sealed class LocalizationService
{
    private readonly string _appRoot;
    private Dictionary<string, string> _strings = new();

    public string CurrentLanguage { get; private set; }

    public LocalizationService(string appRoot, string language)
    {
        _appRoot = appRoot;
        CurrentLanguage = language;
        Load(language);
    }

    public void SwitchLanguage(string language)
    {
        CurrentLanguage = language;
        Load(language);
    }

    public string T(string key) => _strings.TryGetValue(key, out var value) ? value : key;

    private void Load(string language)
    {
        var path = Path.Combine(_appRoot, "Localization", $"strings.{language}.json");
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        else
        {
            _strings = new();
        }
    }
}
