using System.Text;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.FileSystem;

namespace ArchiveManager.Application.Services;

/// <summary>
/// Pure naming engine (spec §7). Deterministic: same inputs always produce the
/// same filename. Intentionally has zero disk/DB access so it is trivially
/// unit-testable — see Tests/FilenameGenerationServiceTests.cs.
/// </summary>
public sealed class FilenameGenerationService
{
    private readonly PersianTextNormalizer _normalizer;

    public FilenameGenerationService(PersianTextNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    /// <summary>
    /// [نوع سند] [موضوع] [سال] [نام شرکت] (نسخه NN).[فرمت]
    /// موضوع is omitted entirely (not a blank gap) when empty.
    /// نسخه is only appended when explicitly provided (never forced to 01).
    /// </summary>
    public string Generate(FilenameParts parts)
    {
        var segments = new System.Collections.Generic.List<string>();

        void AddIfPresent(string? value)
        {
            var sanitized = _normalizer.SanitizeForFilename(value ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                segments.Add(sanitized);
            }
        }

        AddIfPresent(parts.DocumentType);
        AddIfPresent(parts.Subject);
        AddIfPresent(parts.Year.ToString());
        AddIfPresent(parts.CompanyName);

        var baseName = string.Join(' ', segments);

        if (parts.Version is int v && v > 0)
        {
            baseName += $" نسخه {v:D2}";
        }

        var extension = parts.Extension.TrimStart('.').ToLowerInvariant();
        return $"{baseName}.{extension}";
    }

    /// <summary>Same as <see cref="Generate"/> but without the extension — used
    /// when comparing "base names" for duplicate/versioning purposes.</summary>
    public string GenerateBaseName(FilenameParts parts)
    {
        var withVersionStripped = parts with { Version = null };
        var full = Generate(withVersionStripped);
        return full[..^(withVersionStripped.Extension.TrimStart('.').Length + 1)];
    }
}
