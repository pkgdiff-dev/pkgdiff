using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Jobs;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/admin/blog")]
[Authorize]
public class AdminBlogController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAutoPilotService _autoPilotService;
    private readonly IJobScheduler _backgroundJobs;
    private readonly ISystemSettingsReader _settingsReader;
    private readonly IReleaseNotesRenderer _renderer;
    private readonly ILogger<AdminBlogController> _logger;

    public AdminBlogController(
        IUnitOfWork unitOfWork,
        IAutoPilotService autoPilotService,
        IJobScheduler backgroundJobs,
        ISystemSettingsReader settingsReader,
        IReleaseNotesRenderer renderer,
        ILogger<AdminBlogController> logger)
    {
        _unitOfWork = unitOfWork;
        _autoPilotService = autoPilotService;
        _backgroundJobs = backgroundJobs;
        _settingsReader = settingsReader;
        _renderer = renderer;
        _logger = logger;
    }

    [HttpGet]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Blog.View)]
    public async Task<IActionResult> GetAllBlogs([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var (blogs, total) = await _unitOfWork.BlogPosts.GetPageDescendingAsync(
            b => true, b => b.CreatedAt, b => b, page, pageSize);

        return Ok(new PaginatedResult<object> 
        { 
            Data = blogs.Cast<object>().ToList(), 
            Total = total, 
            Page = page, 
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpPost("trigger")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Blog.Manage)]
    public IActionResult TriggerAutoPilot()
    {
        // Ollama can take minutes, so generation runs in Hangfire instead of the request
        _backgroundJobs.Enqueue<IAutoPilotService>(x => x.RunAutoPilotJobAsync());
        return Ok(new { message = "AutoPilot job has been queued in Hangfire." });
    }

    [HttpPut("{id}/toggle-publish")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Blog.Manage)]
    public async Task<IActionResult> TogglePublish(int id)
    {
        var blog = await _unitOfWork.BlogPosts.GetByIdAsync(id);
        if (blog == null) return NotFound();

        var wasPublished = blog.IsPublished;
        blog.IsPublished = !blog.IsPublished;
        if (blog.IsPublished && blog.PublishedAt == null)
        {
            blog.PublishedAt = DateTime.UtcNow;
        }

        await _unitOfWork.BlogPosts.UpdateAsync(blog);
        await _unitOfWork.SaveChangesAsync();

        // Social posting only follows an explicit admin publish; a missing setting means disabled
        var socialPostQueued = false;
        if (!wasPublished && blog.IsPublished)
        {
            var autoPostEnabled = await _settingsReader.GetAsync(SettingKeys.SocialMediaAutoPostEnabled);
            if (string.Equals(autoPostEnabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                _backgroundJobs.Enqueue<BlogSocialPostJob>(j => j.ExecuteAsync(blog.Id));
                socialPostQueued = true;
            }
        }

        return Ok(new { isPublished = blog.IsPublished, socialPostQueued });
    }

    [HttpPut("{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Blog.Manage)]
    public async Task<IActionResult> UpdateBlog(int id, [FromBody] UpdateBlogRequest request)
    {
        var blog = await _unitOfWork.BlogPosts.GetByIdAsync(id);
        if (blog == null) return NotFound();

        blog.Title = request.Title;
        blog.Content = StoredMarkdown.Normalize(request.Content);

        await _unitOfWork.BlogPosts.UpdateAsync(blog);
        await _unitOfWork.SaveChangesAsync();

        return Ok(blog);
    }

    [HttpPost("preview")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Blog.Manage)]
    public IActionResult PreviewBlog([FromBody] BlogPreviewRequest request)
    {
        return Ok(new { html = _renderer.RenderHtml(request.Content) });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = PkgDiff.Shared.Constants.Permissions.Blog.Manage)]
    public async Task<IActionResult> DeleteBlog(int id)
    {
        try
        {
            var blog = await _unitOfWork.BlogPosts.GetByIdAsync(id);
            if (blog == null) return NotFound();

            await _unitOfWork.BlogPosts.DeleteAsync(blog);
            await _unitOfWork.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blog post {BlogPostId}", id);
            return StatusCode(500, new { error = "Failed to delete the blog post." });
        }
    }
}
