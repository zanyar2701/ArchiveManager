namespace ArchiveManager.Domain.Models;

/// <summary>The raw inputs to the naming engine (spec §7).</summary>
public sealed record FilenameParts(
    string DocumentType,
    string? Subject,
    int Year,
    string CompanyName,
    int? Version,
    string Extension);
