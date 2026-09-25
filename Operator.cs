namespace ArchiveManager.Domain.Models;

public enum OperatorRole
{
    Operator = 0,
    Administrator = 1
}

/// <summary>
/// A simple local-user record — not a real auth system (spec §12: "appropriate
/// for a local office tool without a full auth system"). Role gates delete,
/// overwrite-on-duplicate, company creation/level moves, and Settings edits.
/// </summary>
public sealed class Operator
{
    public int OperatorId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public OperatorRole Role { get; set; } = OperatorRole.Operator;
}
