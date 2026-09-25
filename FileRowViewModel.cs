using System;

namespace ArchiveManager.UI.ViewModels;

/// <summary>One row in the center file table. Wraps either a registered
/// Document (rich metadata) or a bare filesystem entry not yet registered
/// (spec §5: unregistered files must still be visible, not hidden).</summary>
public sealed class FileRowViewModel
{
    public int? DocumentId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string AbsolutePath { get; init; } = string.Empty;
    public string TypeLabel { get; init; } = string.Empty; // "PDF", "Word", ...
    public string IconKey { get; init; } = "other";
    public int? Year { get; init; }
    public long SizeBytes { get; init; }
    public DateTime? RegisteredAt { get; init; }
    public bool IsRegistered { get; init; }
    public bool IsMissing { get; init; }

    public string SizeDisplay => SizeBytes < 1024
        ? $"{SizeBytes} B"
        : SizeBytes < 1024 * 1024
            ? $"{SizeBytes / 1024.0:0.0} KB"
            : $"{SizeBytes / 1024.0 / 1024.0:0.0} MB";

    public string YearDisplay => Year?.ToString() ?? "—";
    public string RegisteredAtDisplay => RegisteredAt?.ToString("yyyy/MM/dd") ?? "ثبت نشده";
}
