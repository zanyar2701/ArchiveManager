namespace ArchiveManager.Domain.Models;

/// <summary>
/// One of the four fixed stages a company can be filed under:
/// 01 - رشد مقدماتی, 02 - رشد, 03 - پارکی, 04 - خروج‌یافته.
/// Levels are seeded on first run but the table is not hard-coded in
/// application logic, so the org can rename/reorder via Settings later.
/// </summary>
public sealed class ArchiveLevel
{
    public int ArchiveLevelId { get; set; }
    public string Code { get; set; } = string.Empty;      // "01".."04"
    public string Name { get; set; } = string.Empty;      // "رشد مقدماتی" ...
    public int SortOrder { get; set; }
    public string FolderName => $"{Code} - {Name}";
}
