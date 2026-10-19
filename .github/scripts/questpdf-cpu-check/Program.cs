using System.Reflection;
using System.Runtime.Intrinsics.X86;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

const string Payload = "→ • 🚀 中文 ✓ Ünïcödé";

// Printed before any native call, so the line survives a crash in the native library
Console.WriteLine($"AVX={Avx.IsSupported} AVX2={Avx2.IsSupported}");
Console.Out.Flush();

QuestPDF.Settings.License = LicenseType.Community;

// The missing-glyph switch was renamed between versions; reflection handles both names
var glyphSetting = new[] { "ThrowOnMissingTextGlyphs", "CheckIfAllTextGlyphsAreAvailable" }
    .Select(name => typeof(QuestPDF.Settings).GetProperty(name, BindingFlags.Public | BindingFlags.Static))
    .FirstOrDefault(property => property is { CanWrite: true });
glyphSetting?.SetValue(null, false);

Console.WriteLine($"QUESTPDF={typeof(QuestPDF.Settings).Assembly.GetName().Version} GLYPH_SETTING={glyphSetting?.Name ?? "none"}");
Console.Out.Flush();

// Same structure as ExportService.ExportDiffToPdfAsync
var document = Document.Create(container =>
{
    container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontSize(11));

        page.Header()
            .Text("Sample.Package - Version Comparison")
            .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

        page.Content()
            .PaddingVertical(1, Unit.Centimetre)
            .Column(column =>
            {
                column.Spacing(5);

                column.Item().Text("From: 1.0.0  →  To: 2.0.0").FontSize(12);
                column.Item().Text("Total Versions: 20");

                column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                column.Item().PaddingTop(10).Text("Summary").SemiBold().FontSize(14);
                column.Item().PaddingTop(5).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1);
                    });

                    foreach (var label in new[] { "Breaking Changes", "Bug Fixes", "New Features" })
                    {
                        table.Cell().Element(CellStyle).Text(label);
                        table.Cell().Element(CellStyle).Text("20");
                    }

                    static IContainer CellStyle(IContainer container)
                    {
                        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
                    }
                });

                column.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                column.Item().PaddingTop(10).Text("Changes by Version").SemiBold().FontSize(14);

                for (var index = 1; index <= 20; index++)
                {
                    var version = $"1.{index}.0";
                    column.Item().PaddingTop(15).Column(versionColumn =>
                    {
                        versionColumn.Item().Text($"Version {version}")
                            .SemiBold().FontSize(13).FontColor(Colors.Blue.Darken2);
                        versionColumn.Item().Text("Published: 2026-01-01")
                            .FontSize(9).FontColor(Colors.Grey.Darken1);
                        versionColumn.Item().Text("[Prerelease]")
                            .FontSize(9).FontColor(Colors.Orange.Medium);
                        versionColumn.Item().Text("No release notes available")
                            .Italic().FontColor(Colors.Grey.Medium);

                        versionColumn.Item().PaddingTop(5).Column(categoryColumn =>
                        {
                            categoryColumn.Item().Text("Bug Fixes").SemiBold().FontSize(11);

                            foreach (var entry in new[] { "Fixed a crash", "Improved startup", "Updated dependencies" })
                            {
                                categoryColumn.Item().PaddingLeft(10).Text($"• {entry} {Payload}");
                            }
                        });
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

var pdf = document.GeneratePdf();

if (pdf.Length > 5 && Encoding.ASCII.GetString(pdf, 0, 5) == "%PDF-")
{
    Console.WriteLine($"PDF_OK {pdf.Length}");
    return 0;
}

Console.WriteLine("PDF_INVALID");
return 2;
