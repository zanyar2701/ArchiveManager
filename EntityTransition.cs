namespace ArchiveManager.Domain.Models;

/// <summary>
/// One recorded stage/identity/name transition for an archive entity
/// (EntityTransition table) — the audit record behind "انتقال پرونده" and
/// "تغییر نام". FromStage/ToStage are ArchiveLevelId values; a rename-only
/// operation has FromStageId == ToStageId.
/// </summary>
public sealed class EntityTransition
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public int FromStageId { get; set; }
    public int ToStageId { get; set; }
    public EntityType FromEntityType { get; set; }
    public EntityType ToEntityType { get; set; }
    public string PreviousName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? Reason { get; set; }
    public int? OperatorId { get; set; }

    /// <summary>True when only the stage changed (name/type identical) —
    /// used to render a simpler "مرحله تغییر کرد" history line.</summary>
    public bool IsStageOnly => PreviousName == NewName && FromEntityType == ToEntityType && FromStageId != ToStageId;
    /// <summary>True when only the name/type changed (stage identical).</summary>
    public bool IsRenameOnly => FromStageId == ToStageId;
}
