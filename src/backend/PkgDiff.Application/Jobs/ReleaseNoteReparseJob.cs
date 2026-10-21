using System.Text.Json;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.ReleaseNotes;

namespace PkgDiff.Application.Jobs;

// Stored categories are built when notes are scraped; a parser change re-parses them once, a package per job
public class ReleaseNoteReparseJob
{
    // Raise when the release-note parser changes what it returns
    public const string ParserVersion = "2";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAnalyzerService _analyzer;
    private readonly IJobScheduler _jobs;
    private readonly ILogger<ReleaseNoteReparseJob> _logger;

    public ReleaseNoteReparseJob(
        IUnitOfWork unitOfWork,
        IAnalyzerService analyzer,
        IJobScheduler jobs,
        ILogger<ReleaseNoteReparseJob> logger)
    {
        _unitOfWork = unitOfWork;
        _analyzer = analyzer;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var setting = (await _unitOfWork.SystemSettings.FindAsync(s => s.Key == SettingKeys.ReleaseNoteParserVersion)).FirstOrDefault();
        if (setting?.Value == ParserVersion)
            return;

        var packageIds = await _unitOfWork.ToListAsync(_unitOfWork.CachedPackages.Query().Select(p => p.Id));
        foreach (var packageId in packageIds)
            _jobs.Enqueue<ReleaseNoteReparseJob>(job => job.ReparsePackageAsync(packageId));

        if (setting is null)
        {
            var definition = SettingKeys.Find(SettingKeys.ReleaseNoteParserVersion)!;
            await _unitOfWork.SystemSettings.AddAsync(new SystemSettings
            {
                Key = definition.Key,
                Value = ParserVersion,
                Description = definition.Description,
                DataType = definition.DataType,
                Category = definition.Category,
                IsSecret = definition.IsSecret
            });
        }
        else
        {
            setting.Value = ParserVersion;
        }
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Re-parsing the stored release notes of {Count} packages for parser {Version}", packageIds.Count, ParserVersion);
    }

    public async Task ReparsePackageAsync(int cachedPackageId)
    {
        var notes = await _unitOfWork.CachedReleaseNotes.FindAsync(n => n.CachedPackageId == cachedPackageId && !n.IsEmpty);
        foreach (var note in notes)
        {
            note.ParsedNotesJson = JsonSerializer.Serialize(
                _analyzer.ParseReleaseNotes(CompiledReleaseNotes.Split(note.ReleaseNotes).Body));
        }
        await _unitOfWork.SaveChangesAsync();
    }
}
