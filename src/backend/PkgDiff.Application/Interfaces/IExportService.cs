using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IExportService
{
    Task<byte[]> ExportToPdfAsync(PackageMetadata metadata);

    Task<byte[]> ExportDiffToPdfAsync(AnalysisResult diff);
}
