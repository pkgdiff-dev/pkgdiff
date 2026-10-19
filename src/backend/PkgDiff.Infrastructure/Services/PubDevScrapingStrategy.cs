using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

public class PubDevScrapingStrategy : IReleaseNotesScrapingStrategy
{
    private readonly IPubDevService _pubDevService;
    private readonly ILogger<PubDevScrapingStrategy> _logger;

    public PubDevScrapingStrategy(
        IPubDevService pubDevService,
        ILogger<PubDevScrapingStrategy> logger)
    {
        _pubDevService = pubDevService;
        _logger = logger;
    }

    public async Task<ReleaseNotesResult> GetReleaseNotesAsync(string packageId, string version, string? githubRepoUrl, IRegistryClient registry)
    {
        _logger.LogDebug("[PubDevStrategy] Scraping pub.dev changelog for {PackageId} v{Version}", packageId, version);

        var releaseNotes = await _pubDevService.GetReleaseNotesAsync(packageId, version);
        var notesContent = releaseNotes ?? string.Empty;

        return new ReleaseNotesResult(notesContent, "PubDev", null);
    }
}
