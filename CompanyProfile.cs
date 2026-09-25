namespace ArchiveManager.Domain.Models;

/// <summary>
/// Optional legal/registration details, present once an entity is (or becomes)
/// a Company (spec addendum §"COMPANY REGISTRATION INFORMATION"). Never
/// required for a Person in pre-growth.
/// </summary>
public sealed class CompanyProfile
{
    public int EntityId { get; set; } // = Company.CompanyId, 1:1
    public string? NationalId { get; set; }        // شناسه ملی
    public string? RegistrationNumber { get; set; } // شماره ثبت
    public DateTime? RegistrationDate { get; set; }  // تاریخ ثبت
    public string? CompanyType { get; set; }         // نوع شرکت
    public string? Ceo { get; set; }                 // نام مدیرعامل
    public string? ContactInformation { get; set; }
    public string? Website { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(NationalId) &&
        !string.IsNullOrWhiteSpace(RegistrationNumber) &&
        RegistrationDate is not null;
}
