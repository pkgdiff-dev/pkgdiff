using System.Text;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Security;
using PkgDiff.Shared.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PkgDiff.Application.Services;

public class ExportService : IExportService
{
    // QuestPDF settings are process-wide: set them once, not on every request that resolves this scoped service
    static ExportService()
    {
        // Configure QuestPDF license (Community license for open source projects)
        QuestPDF.Settings.License = LicenseType.Community;

        // Release notes carry arbitrary Unicode; a glyph missing from the bundled font must not fail the export
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    #region Package Metadata Export

    public Task<byte[]> ExportToPdfAsync(PackageMetadata metadata)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header()
                    .Text($"{metadata.PackageId} - Release Notes")
                    .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        column.Spacing(5);

                        // Package Info
                        column.Item().Text($"Latest Version: {metadata.LatestVersion}").FontSize(12);
                        if (!string.IsNullOrEmpty(metadata.Description))
                        {
                            column.Item().Text($"Description: {metadata.Description}");
                        }
                        if (!string.IsNullOrEmpty(metadata.Authors))
                        {
                            column.Item().Text($"Authors: {metadata.Authors}");
                        }

                        column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        ComposePackageSecurity(column, metadata);

                        // Versions
                        foreach (var version in metadata.Versions)
                        {
                            column.Item().PaddingTop(15).Column(versionColumn =>
                            {
                                versionColumn.Item().Text($"Version {version.Version}")
                                    .SemiBold().FontSize(14).FontColor(Colors.Blue.Darken2);

                                if (version.PublishedDate != null)
                                {
                                    versionColumn.Item().Text($"Published: {version.PublishedDate}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                                }

                                if (version.IsPrerelease)
                                {
                                    versionColumn.Item().Text("[Prerelease]")
                                        .FontSize(9).FontColor(Colors.Orange.Medium);
                                }

                                versionColumn.Item().PaddingTop(5);

                                if (version.IsEmpty)
                                {
                                    versionColumn.Item().Text("No release notes available")
                                        .Italic().FontColor(Colors.Grey.Medium);
                                }
                                else if (version.Entries.Any())
                                {
                                    var groupedEntries = version.Entries.GroupBy(e => e.Category);
                                    foreach (var group in groupedEntries)
                                    {
                                        versionColumn.Item().PaddingTop(5).Column(categoryColumn =>
                                        {
                                            categoryColumn.Item().Text(GetCategoryName(group.Key))
                                                .SemiBold().FontSize(11);

                                            foreach (var entry in group)
                                            {
                                                categoryColumn.Item().PaddingLeft(10).Text($"• {entry.Text}");
                                            }
                                        });
                                    }
                                }
                                else if (!string.IsNullOrEmpty(version.ReleaseNotes))
                                {
                                    versionColumn.Item().Text(version.ReleaseNotes);
                                }
                            });
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    #endregion

    #region Diff Export

    public Task<byte[]> ExportDiffToPdfAsync(AnalysisResult diff)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header()
                    .Text($"{diff.PackageId} - Version Comparison")
                    .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                page.Content()
                    .PaddingVertical(1, Unit.Centimetre)
                    .Column(column =>
                    {
                        column.Spacing(5);

                        // Diff Info
                        column.Item().Text($"From: {diff.FromVersion}  →  To: {diff.ToVersion}").FontSize(12);
                        column.Item().Text($"Total Versions: {diff.TotalVersions}");

                        column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Summary
                        column.Item().PaddingTop(10).Text("Summary").SemiBold().FontSize(14);
                        column.Item().PaddingTop(5).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                            });

                            table.Cell().Element(CellStyle).Text("Breaking Changes");
                            table.Cell().Element(CellStyle).Text(diff.Summary.BreakingChanges.ToString());

                            table.Cell().Element(CellStyle).Text("Bug Fixes");
                            table.Cell().Element(CellStyle).Text(diff.Summary.BugFixes.ToString());

                            table.Cell().Element(CellStyle).Text("New Features");
                            table.Cell().Element(CellStyle).Text(diff.Summary.NewFeatures.ToString());

                            table.Cell().Element(CellStyle).Text("Performance Improvements");
                            table.Cell().Element(CellStyle).Text(diff.Summary.PerformanceImprovements.ToString());

                            table.Cell().Element(CellStyle).Text("Security Fixes");
                            table.Cell().Element(CellStyle).Text(diff.Summary.SecurityFixes.ToString());

                            table.Cell().Element(CellStyle).Text("Deprecations");
                            table.Cell().Element(CellStyle).Text(diff.Summary.Deprecations.ToString());

                            static IContainer CellStyle(IContainer container)
                            {
                                return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
                            }
                        });

                        column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        ComposeUpgradeSecurity(column, diff);

                        // Versions
                        column.Item().PaddingTop(10).Text("Changes by Version").SemiBold().FontSize(14);

                        foreach (var version in diff.Versions)
                        {
                            column.Item().PaddingTop(15).Column(versionColumn =>
                            {
                                versionColumn.Item().Text($"Version {version.Version}")
                                    .SemiBold().FontSize(13).FontColor(Colors.Blue.Darken2);

                                if (version.PublishedDate != null)
                                {
                                    versionColumn.Item().Text($"Published: {version.PublishedDate}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                                }

                                if (version.IsPrerelease)
                                {
                                    versionColumn.Item().Text("[Prerelease]")
                                        .FontSize(9).FontColor(Colors.Orange.Medium);
                                }

                                versionColumn.Item().PaddingTop(5);

                                if (version.IsEmpty)
                                {
                                    versionColumn.Item().Text("No release notes available")
                                        .Italic().FontColor(Colors.Grey.Medium);
                                }
                                else if (version.Entries.Any())
                                {
                                    var groupedEntries = version.Entries.GroupBy(e => e.Category);
                                    foreach (var group in groupedEntries)
                                    {
                                        versionColumn.Item().PaddingTop(5).Column(categoryColumn =>
                                        {
                                            categoryColumn.Item().Text(GetCategoryName(group.Key))
                                                .SemiBold().FontSize(11);

                                            foreach (var entry in group)
                                            {
                                                categoryColumn.Item().PaddingLeft(10).Text($"• {entry.Text}");
                                            }
                                        });
                                    }
                                }
                                else if (!string.IsNullOrEmpty(version.ReleaseNotes))
                                {
                                    versionColumn.Item().Text(version.ReleaseNotes);
                                }
                            });
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    #endregion

    #region Security

    private static void ComposePackageSecurity(ColumnDescriptor column, PackageMetadata metadata)
    {
        var trust = metadata.Trust;
        if (trust is null || trust.Status != TrustStatus.Ready)
            return;

        column.Item().PaddingTop(10).Text("Security").SemiBold().FontSize(14);
        ComposeMalwareWarning(column, trust, metadata.Versions.Select(v => v.Version));
        if (!string.IsNullOrEmpty(metadata.LatestVersion))
            ComposeAdvisories(column, $"Known vulnerabilities in {metadata.LatestVersion}", TrustDiff.AdvisoriesOf(trust, metadata.LatestVersion));
        ComposeHealth(column, trust.Health);
        column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
    }

    private static void ComposeUpgradeSecurity(ColumnDescriptor column, AnalysisResult diff)
    {
        var trust = diff.Trust;
        if (trust is null || trust.Status != TrustStatus.Ready)
            return;

        column.Item().PaddingTop(10).Text("Security of this upgrade").SemiBold().FontSize(14);
        ComposeMalwareWarning(column, trust, diff.Versions.Select(v => v.Version).Append(diff.FromVersion).Append(diff.ToVersion));
        ComposeAdvisories(column, $"Fixed by upgrading to {diff.ToVersion}", TrustDiff.Fixed(trust, diff.FromVersion, diff.ToVersion));
        ComposeAdvisories(column, $"Introduced by upgrading to {diff.ToVersion}", TrustDiff.Introduced(trust, diff.FromVersion, diff.ToVersion));
        ComposeHealth(column, trust.Health);
        column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
    }

    private static void ComposeMalwareWarning(ColumnDescriptor column, PackageTrust trust, IEnumerable<string> versions)
    {
        var malicious = TrustDiff.MaliciousVersions(trust, versions.Distinct());
        if (malicious.Count == 0)
            return;
        column.Item().PaddingTop(5).Background(Colors.Red.Lighten4).Padding(6)
            .Text($"Malicious package warning: {string.Join(", ", malicious)} matched a malicious-package record (OSV MAL-). Do not install these versions.")
            .FontColor(Colors.Red.Darken3).SemiBold();
    }

    private static void ComposeAdvisories(ColumnDescriptor column, string title, IReadOnlyList<SecurityAdvisory> advisories)
    {
        column.Item().PaddingTop(5).Text($"{title}: {advisories.Count}").SemiBold();
        foreach (var advisory in advisories)
        {
            var fixedIn = advisory.FixedIn.Count > 0 ? $" (fixed in {string.Join(", ", advisory.FixedIn)})" : string.Empty;
            var aliases = advisory.Aliases.Count > 0 ? $" [{string.Join(", ", advisory.Aliases)}]" : string.Empty;
            column.Item().PaddingLeft(10).Text($"• {advisory.Id}{aliases} — {advisory.Severity}{fixedIn}: {advisory.Summary}").FontSize(9);
        }
    }

    private static void ComposeHealth(ColumnDescriptor column, PackageHealth? health)
    {
        if (health is null)
            return;
        var score = health.ScorecardScore is { } value ? $"{value:0.0}/10" : "not available";
        column.Item().PaddingTop(5).Text($"OpenSSF Scorecard: {score}").SemiBold();
        if (health.Licenses.Count > 0)
            column.Item().Text($"Licenses: {string.Join(", ", health.Licenses)}");
        if (health.HasProvenance is { } provenance)
            column.Item().Text(provenance ? "Provenance: attestation found" : "Provenance: no attestation found");
        if (!string.IsNullOrEmpty(health.Attribution))
            column.Item().Text(health.Attribution).FontSize(8).FontColor(Colors.Grey.Darken1);
    }

    #endregion

    #region Helper Methods

    private static string GetCategoryName(ChangeCategory category)
    {
        return category switch
        {
            ChangeCategory.BreakingChange => "Breaking Changes",
            ChangeCategory.BugFix => "Bug Fixes",
            ChangeCategory.NewFeature => "New Features",
            ChangeCategory.Performance => "Performance Improvements",
            ChangeCategory.Security => "Security Fixes",
            ChangeCategory.Deprecation => "Deprecations",
            _ => "Other"
        };
    }

    #endregion
}
