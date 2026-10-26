using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.API.Services;

public class ScrapeBudget : IScrapeBudget
{
    // Caps first-visit scrapes so a stream of new names cannot fill the queue or the GitHub budget
    private const int MaxScrapesPerAddressPerHour = 30;
    private const int MaxScrapesPerHour = 600;

    private readonly IRateLimitCounterStore _counters;
    private readonly ILogger<ScrapeBudget> _logger;

    public ScrapeBudget(IRateLimitCounterStore counters, ILogger<ScrapeBudget> logger)
    {
        _counters = counters;
        _logger = logger;
    }

    public async Task<bool> TryReserveAsync(string ipAddress)
    {
        var mine = await _counters.IncrementAsync($"scrape-queue:1h:{ipAddress}", TimeSpan.FromHours(1));
        var all = await _counters.IncrementAsync("scrape-queue:1h:all", TimeSpan.FromHours(1));
        if (mine <= MaxScrapesPerAddressPerHour && all <= MaxScrapesPerHour)
            return true;

        _logger.LogWarning("Scrape not queued: {Address} queued {Mine} this hour, {All} in total", ipAddress, mine, all);
        return false;
    }
}
