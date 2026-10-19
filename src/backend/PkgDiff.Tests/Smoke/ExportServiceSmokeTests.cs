using System.Text;
using PkgDiff.Application.Services;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Tests.Smoke;

public class ExportServiceSmokeTests
{
    // Release notes are untrusted text; these characters are not in the bundled PDF font.
    private const string UnicodeText = "Faster startup → fewer allocations • 🚀 中文 ✓ Привет";

    [Fact]
    public async Task ExportToPdfAsync_ReturnsPdf_WhenReleaseNotesContainUnicode()
    {
        var metadata = new PackageMetadata
        {
            PackageId = "Sample.Package",
            LatestVersion = "2.0.0",
            Description = $"Description {UnicodeText}",
            Authors = "Jürgen Müller, 山田太郎",
            Versions =
            [
                new VersionSummary
                {
                    Version = "2.0.0",
                    PublishedDate = new DateTime(2026, 1, 15),
                    IsEmpty = false,
                    Entries =
                    [
                        new ReleaseNoteEntry { Category = ChangeCategory.NewFeature, Text = UnicodeText },
                        new ReleaseNoteEntry { Category = ChangeCategory.BugFix, Text = $"Fixed {UnicodeText}" }
                    ]
                },
                new VersionSummary
                {
                    Version = "1.1.0-beta.1",
                    IsPrerelease = true,
                    IsEmpty = false,
                    ReleaseNotes = $"Raw notes {UnicodeText}"
                },
                new VersionSummary { Version = "1.0.0" }
            ]
        };

        var pdf = await new ExportService().ExportToPdfAsync(metadata);

        AssertStartsWithPdfHeader(pdf);
        Assert.True(pdf.Length > 1024, $"Expected more than 1 KB, got {pdf.Length} bytes.");
    }

    [Fact]
    public async Task ExportDiffToPdfAsync_ReturnsPdf_WhenEntriesInSeveralCategoriesContainUnicode()
    {
        var categories = new[]
        {
            ChangeCategory.BreakingChange,
            ChangeCategory.BugFix,
            ChangeCategory.NewFeature,
            ChangeCategory.Security,
            ChangeCategory.Other
        };

        var diff = new AnalysisResult
        {
            PackageId = "Sample.Package",
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            TotalVersions = 2,
            Summary = new Summary { BreakingChanges = 2, BugFixes = 2, NewFeatures = 2, SecurityFixes = 2, OtherChanges = 2 },
            Versions =
            [
                new VersionInfo
                {
                    Version = "2.0.0",
                    PublishedDate = new DateTime(2026, 1, 15),
                    Categories = categories.ToList(),
                    Entries = categories
                        .Select(category => new ReleaseNoteEntry { Category = category, Text = UnicodeText })
                        .ToList()
                },
                new VersionInfo
                {
                    Version = "1.1.0-beta.1",
                    IsPrerelease = true,
                    Categories = categories.ToList(),
                    Entries = categories
                        .Select(category => new ReleaseNoteEntry { Category = category, Text = $"{category}: {UnicodeText}" })
                        .ToList()
                }
            ]
        };

        var pdf = await new ExportService().ExportDiffToPdfAsync(diff);

        AssertStartsWithPdfHeader(pdf);
    }

    [Fact]
    public async Task ExportToPdfAsync_ReturnsPdf_WhenThereAreNoVersions()
    {
        var metadata = new PackageMetadata { PackageId = "Sample.Package", LatestVersion = "1.0.0" };

        var pdf = await new ExportService().ExportToPdfAsync(metadata);

        AssertStartsWithPdfHeader(pdf);
    }

    private static void AssertStartsWithPdfHeader(byte[] pdf)
    {
        Assert.True(pdf.Length > 5, $"Expected a PDF document, got {pdf.Length} bytes.");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }
}
