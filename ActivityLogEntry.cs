namespace ArchiveManager.Domain.Models;

/// <summary>One row in the audit trail. DetailsJson carries before/after state
/// so an Administrator can reverse a rename (spec §13, "بازگردانی"). </summary>
public sealed class ActivityLogEntry
{
    public int LogId { get; set; }
    public DateTime Timestamp { get; set; }
    public int? OperatorId { get; set; }
    public string Action { get; set; } = string.Empty; // "ثبت سند" | "تغییر نام" | ...
    public int? DocumentId { get; set; }
    public string? DetailsJson { get; set; }
}
