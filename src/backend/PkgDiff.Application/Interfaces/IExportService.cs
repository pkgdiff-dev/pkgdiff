using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IExportService
{
    /// <summary>
    /// Export all release notes for a package to PDF format
    /// </summary>
    Task<byte[]> ExportToPdfAsync(PackageMetadata metadata);

    /// <summary>
    /// Export diff analysis results to PDF format
    /// </summary>
    Task<byte[]> ExportDiffToPdfAsync(AnalysisResult diff);
}
