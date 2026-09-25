namespace ArchiveManager.Domain.Models;

/// <summary>Known file format → extension/label/icon mapping. Spec §8.</summary>
public sealed class FileFormat
{
    public int FileFormatId { get; set; }
    public string Extension { get; set; } = string.Empty; // "pdf", "docx", ...
    public string Label { get; set; } = string.Empty;      // "PDF", "Word", ...
    public string IconKey { get; set; } = string.Empty;    // key into the icon dictionary
}
