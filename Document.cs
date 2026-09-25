namespace ArchiveManager.Domain.Models;

/// <summary>
/// A single registered document. This is metadata only — RelativePath always
/// points back to the real file on the real archive drive; the database never
/// stores a copy of file contents (spec §18/§16-build).
/// </summary>
public sealed class Document
{
    public int DocumentId { get; set; }
    public int CompanyId { get; set; }
    public int DocumentTypeId { get; set; }
    public int? SubjectId { get; set; }
    public int Year { get; set; }
    public int? Version { get; set; }
    public int FileFormatId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime RegisteredAt { get; set; }
    public int? RegisteredByOperatorId { get; set; }
    public bool IsMissing { get; set; }

    // Convenience fields populated by joins for display — not persisted directly.
    public string? CompanyName { get; set; }
    public string? DocumentTypeName { get; set; }
    public string? SubjectName { get; set; }
    public string? FormatLabel { get; set; }
}
