using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAnalyzerService
{
    List<ReleaseNoteEntry> ParseReleaseNotes(string releaseNotes);
    List<ChangeCategory> CategorizeReleaseNotes(string releaseNotes);
    Summary GenerateSummary(List<VersionInfo> versions);
    IEnumerable<string> ExtractMentions(string text);
}
