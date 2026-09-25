namespace ArchiveManager.Domain.Models;

/// <summary>Configurable subject list (استقرار, ارزیابی, ...). Spec §38.</summary>
public sealed class DocumentSubject
{
    public int SubjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
