using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContributorsController : ControllerBase
{
    private readonly IContributorService _contributorService;
    private readonly IJobScheduler _jobs;

    public ContributorsController(IContributorService contributorService, IJobScheduler jobs)
    {
        _contributorService = contributorService;
        _jobs = jobs;
    }

    [HttpGet]
    public async Task<IActionResult> GetLeaderboard([FromQuery] int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var leaderboard = await _contributorService.GetLeaderboardAsync(limit);
        return Ok(leaderboard);
    }

    [HttpGet("paged")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Contributors.Manage)]
    public async Task<IActionResult> GetLeaderboardPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _contributorService.GetLeaderboardPagedAsync(page, pageSize);
        return Ok(new
        {
            data = items,
            page,
            pageSize,
            total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpPost("backfill")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Contributors.Manage)]
    public IActionResult TriggerBackfill()
    {
        _jobs.Enqueue<IContributorService>(svc => svc.BackfillFromExistingReleaseNotesAsync());
        return Ok(new { message = "Backfill job enqueued. Check Hangfire dashboard for progress." });
    }
}
