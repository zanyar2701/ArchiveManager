namespace ArchiveManager.Domain.Models;

/// <summary>
/// PERSON: a pre-growth applicant archived under an individual's name (spec
/// "entity identity management" addendum). COMPANY: a legally registered entity.
/// </summary>
public enum EntityType
{
    Person = 0,
    Company = 1
}

/// <summary>
/// An archive entity — the stable, internal-ID-anchored record behind a folder
/// in the tree. This class already WAS the archive entity (CompanyId is the
/// permanent internal ID referenced by Documents, ActivityLog, etc.); it is
/// extended in place rather than duplicated, per the addendum's instruction
/// not to build a parallel architecture. The class keeps its original name
/// ("Company") for backward compatibility with all existing code/queries —
/// it now represents a Person OR a Company, distinguished by EntityType.
/// FolderPath is always relative to the configured archive root — never a
/// hard-coded drive letter (spec §17/§19).
/// </summary>
public sealed class Company
{
    public int CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;                // = CurrentName
    public int ArchiveLevelId { get; set; }                          // = CurrentStageId
    public string FolderPath { get; set; } = string.Empty;
    public int? PreviousLevelId { get; set; }   // set when a company moves to خروج‌یافته (spec §25)
    public DateTime CreatedAt { get; set; }

    // --- Added for entity identity management (addendum) ---
    public EntityType EntityType { get; set; } = EntityType.Company;
    public string ArchiveCode { get; set; } = string.Empty;          // stable "A-0001" code, never changes
    public DateTime UpdatedAt { get; set; }
}
