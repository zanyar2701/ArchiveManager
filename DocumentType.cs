namespace ArchiveManager.Domain.Models;

/// <summary>Configurable document-type list (قرارداد, گزارش, صورتجلسه, ...). Spec §37.</summary>
public sealed class DocumentType
{
    public int DocumentTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
