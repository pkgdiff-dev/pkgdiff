using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BlogController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IReleaseNotesRenderer _renderer;

    public BlogController(IUnitOfWork unitOfWork, IReleaseNotesRenderer renderer)
    {
        _unitOfWork = unitOfWork;
        _renderer = renderer;
    }

    [HttpGet]
    public async Task<IActionResult> GetPublishedBlogs([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var (blogs, total) = await _unitOfWork.BlogPosts.GetPageDescendingAsync(
            b => b.IsPublished,
            b => b.PublishedAt ?? b.CreatedAt,
            b => new
            {
                b.Id,
                b.Title,
                b.Slug,
                b.PackageId,
                b.Version,
                b.PublishedAt,
                Excerpt = b.Content.Length > 200 ? b.Content.Substring(0, 200) + "..." : b.Content
            },
            page,
            pageSize);

        return Ok(new PaginatedResult<object> 
        { 
            Data = blogs.Cast<object>().ToList(), 
            Total = total, 
            Page = page, 
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBlogBySlug(string slug)
    {
        var blog = (await _unitOfWork.BlogPosts.FindAsync(b => b.Slug == slug && b.IsPublished)).FirstOrDefault();
        
        if (blog == null)
            return NotFound(new { error = "Blog post not found." });

        return Ok(new {
            blog.Id, blog.Title, blog.Slug, blog.Content,
            ContentHtml = _renderer.RenderHtml(blog.Content),
            blog.PackageId, blog.Version, blog.Registry,
            blog.IsPublished, blog.CreatedAt, blog.PublishedAt
        });
    }
}
