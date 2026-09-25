using ArchiveManager.Application.Services;
using ArchiveManager.Domain.Models;
using ArchiveManager.Infrastructure.FileSystem;
using Xunit;

namespace ArchiveManager.Tests;

/// <summary>
/// Validates the naming engine (spec §7) against every worked example in the
/// specification, plus the version/duplicate/normalization edge cases.
/// </summary>
public class FilenameGenerationServiceTests
{
    private readonly FilenameGenerationService _service = new(new PersianTextNormalizer());

    [Fact]
    public void Generate_BasicContract_MatchesSpecExample()
    {
        var parts = new FilenameParts("قرارداد", "استقرار", 1404, "اوان", null, "pdf");
        Assert.Equal("قرارداد استقرار 1404 اوان.pdf", _service.Generate(parts));
    }

    [Fact]
    public void Generate_RenewalContract_MatchesSpecExample()
    {
        var parts = new FilenameParts("قرارداد", "تمدید استقرار", 1404, "اوان", null, "pdf");
        Assert.Equal("قرارداد تمدید استقرار 1404 اوان.pdf", _service.Generate(parts));
    }

    [Fact]
    public void Generate_EvaluationReport_MatchesSpecExample()
    {
        var parts = new FilenameParts("گزارش", "ارزیابی", 1404, "اوان", null, "pdf");
        Assert.Equal("گزارش ارزیابی 1404 اوان.pdf", _service.Generate(parts));
    }

    [Fact]
    public void Generate_MeetingMinutes_MatchesSpecExample()
    {
        var parts = new FilenameParts("صورتجلسه", "کارگروه", 1404, "اوان", null, "pdf");
        Assert.Equal("صورتجلسه کارگروه 1404 اوان.pdf", _service.Generate(parts));
    }

    [Fact]
    public void Generate_WithVersion_AppendsZeroPaddedVersion()
    {
        var parts = new FilenameParts("قرارداد", "استقرار", 1404, "اوان", 2, "pdf");
        Assert.Equal("قرارداد استقرار 1404 اوان نسخه 02.pdf", _service.Generate(parts));
    }

    [Fact]
    public void Generate_WithoutSubject_OmitsSegmentEntirely_NoDoubleSpace()
    {
        var parts = new FilenameParts("مجوز", null, 1404, "اوان", null, "pdf");
        Assert.Equal("مجوز 1404 اوان.pdf", _service.Generate(parts));
        Assert.DoesNotContain("  ", _service.Generate(parts));
    }

    [Fact]
    public void Generate_WithInvalidWindowsCharacters_StripsThem()
    {
        var parts = new FilenameParts("قرارداد", "است:قرار*", 1404, "اوان", null, "pdf");
        var result = _service.Generate(parts);
        Assert.DoesNotContain(':', result);
        Assert.DoesNotContain('*', result);
    }

    [Fact]
    public void Generate_MixedPersianEnglishCompanyName_PassesThrough()
    {
        var parts = new FilenameParts("قرارداد", "استقرار", 1404, "TechCo", null, "pdf");
        Assert.Equal("قرارداد استقرار 1404 TechCo.pdf", _service.Generate(parts));
    }

    [Fact]
    public void Generate_ArabicCharacterVariants_NormalizedToPersian()
    {
        // ي (Arabic) and ك (Arabic) should normalize to ی and ک (Persian)
        var parts = new FilenameParts("قرارداد", "استقرار", 1404, "شركت", null, "pdf");
        var result = _service.Generate(parts);
        Assert.Contains("شرکت", result);
        Assert.DoesNotContain("شركت", result);
    }

    [Fact]
    public void NextFreeVersion_NoExistingFiles_ReturnsTwo()
    {
        var duplicateService = new DuplicateDetectionService();
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            var next = duplicateService.NextFreeVersion(tempDir, "قرارداد استقرار 1404 اوان", "pdf");
            Assert.Equal(2, next);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }
}
