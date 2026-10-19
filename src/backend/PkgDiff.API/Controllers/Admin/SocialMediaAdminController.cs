using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers.Admin;

[Authorize]
[ApiController]
[Route("api/admin/social-media")]
public class SocialMediaAdminController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISocialMediaService _socialMediaService;
    private readonly ILogger<SocialMediaAdminController> _logger;

    public SocialMediaAdminController(IUnitOfWork unitOfWork, ISocialMediaService socialMediaService, ILogger<SocialMediaAdminController> logger)
    {
        _unitOfWork = unitOfWork;
        _socialMediaService = socialMediaService;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.SocialMedia.View)]
    public async Task<ActionResult<IEnumerable<object>>> GetHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var query = _unitOfWork.SocialMediaPosts.Query()
            .OrderByDescending(p => p.CreatedAt)
            .Include(p => p.BlogPost);

        var totalCount = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var result = items.Select(i => new
        {
            i.Id,
            Platform = i.Platform.ToString(),
            i.Content,
            i.PostUrl,
            Status = i.Status.ToString(),
            i.ErrorMessage,
            i.CreatedAt,
            i.BlogPostId,
            BlogTitle = i.BlogPost?.Title
        });

        return Ok(new { Items = result, TotalCount = totalCount });
    }

    [HttpPost("share")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.SocialMedia.Manage)]
    public async Task<ActionResult> ManualShare([FromBody] ManualShareRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content) || !Enum.TryParse<SocialMediaPlatform>(request.Platform, true, out var platform)
            || !Enum.IsDefined(platform))
        {
            return BadRequest("Invalid payload. Content is required and Platform must be Twitter or LinkedIn.");
        }

        try
        {
            var result = platform == SocialMediaPlatform.Twitter
                ? await _socialMediaService.PostToTwitterAsync(request.Content)
                : await _socialMediaService.PostToLinkedInAsync(request.Content);

            var record = new SocialMediaPost
            {
                Platform = platform,
                Content = request.Content,
                Status = result.Outcome switch
                {
                    SocialPostOutcome.Succeeded => SocialMediaPostStatus.Success,
                    SocialPostOutcome.Rejected => SocialMediaPostStatus.Failed,
                    _ => SocialMediaPostStatus.Pending
                },
                PostUrl = result.PostUrl,
                ErrorMessage = result.ErrorMessage
            };

            await _unitOfWork.SocialMediaPosts.AddAsync(record);
            await _unitOfWork.SaveChangesAsync();

            if (result.Outcome == SocialPostOutcome.Unknown)
            {
                return StatusCode(502, new { message = "It is unknown whether the post reached " + platform + ". Check the account before posting again.", error = result.ErrorMessage });
            }

            if (!result.Success)
            {
                return BadRequest(new { message = "Failed to post to " + platform, error = result.ErrorMessage });
            }

            return Ok(new { message = "Successfully posted to " + platform, url = result.PostUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual share to social media failed.");
            return StatusCode(500, new { error = "An internal server error occurred." });
        }
    }

    [HttpGet("linkedin/auth-url")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.SocialMedia.Manage)]
    public async Task<ActionResult> GetLinkedInAuthUrl()
    {
        var userId = GetCallerId();
        if (userId == null)
            return Unauthorized(new { error = "Unable to identify the current user." });

        try
        {
            // Request.Scheme is already rewritten by the trusted forwarded-headers middleware
            var redirectUri = $"{Request.Scheme}://{Request.Host}/admin/social-media/linkedin/callback";
            var result = await _socialMediaService.GetLinkedInAuthUrlAsync(redirectUri, userId);

            if (result == null)
                return BadRequest(new { error = "LinkedIn ClientId is not configured. Please add 'SocialMedia:LinkedIn:ClientId' to System Settings." });

            return Ok(new { authUrl = result.Value.AuthUrl, redirectUri, state = result.Value.State });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the LinkedIn authorization URL");
            return StatusCode(500, new { error = "Could not start the LinkedIn authorization." });
        }
    }

    [HttpPost("linkedin/callback")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.SocialMedia.Manage)]
    public async Task<ActionResult> HandleLinkedInCallback([FromBody] LinkedInCallbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { error = "Authorization code is required." });

        if (string.IsNullOrWhiteSpace(request.State))
            return BadRequest(new { error = "Authorization state is required." });

        var userId = GetCallerId();
        if (userId == null)
            return Unauthorized(new { error = "Unable to identify the current user." });

        var result = await _socialMediaService.HandleLinkedInCallbackAsync(request.Code, request.State, userId);

        if (!result.Success)
            return BadRequest(new { error = result.ErrorMessage });

        return Ok(new { message = "LinkedIn connected successfully! AccessToken and AuthorURN have been saved to System Settings." });
    }

    private string? GetCallerId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}

public class ManualShareRequest
{
    public string Platform { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class LinkedInCallbackRequest
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

