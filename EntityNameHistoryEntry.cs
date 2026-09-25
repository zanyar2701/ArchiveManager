namespace ArchiveManager.Domain.Models;

public enum NameType { Person = 0, Company = 1 }

/// <summary>
/// One name a given archive entity has ever been known by (EntityNameHistory
/// table). Exactly one row per entity has IsCurrent = true. Old rows remain
/// forever so search and history can resolve a previous name back to the
/// same entity (addendum: "search must handle old and new names").
/// </summary>
public sealed class EntityNameHistoryEntry
{
    public int Id { get; set; }
    public int EntityId { get; set; }   // = Company.CompanyId
    public string Name { get; set; } = string.Empty;
    public NameType NameType { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool IsCurrent { get; set; }
}
