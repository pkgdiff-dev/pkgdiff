using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.API.Controllers;
using PkgDiff.API.Services;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Content;

public class RenderedContentApiTests
{
    private const string Notes = "- Fix `List<T>` <img src=x onerror=alert(1)> in https://github.com/o/r/pull/1";

    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ReleaseNotesRenderer _renderer = new();

    [Fact]
    public async Task Cached_metadata_carries_sanitized_html_for_notes_and_entries()
    {
        await SeedCachedPackageAsync();
        var service = new PackageCacheService(
            _unitOfWork, TestRegistries.Create(), _renderer, null!, new CachedLookups(_unitOfWork, new MemoryCache(new MemoryCacheOptions())), null!, NullLogger<PackageCacheService>.Instance);

        var metadata = await service.GetOrFetchMetadataAsync("nuget", "My.Pkg");

        var version = Assert.Single(metadata.Versions);
        Assert.Equal(Notes, version.ReleaseNotes);
        Assert.Equal(_renderer.RenderHtml(Notes), version.ReleaseNotesHtml);
        Assert.Contains("<code>List&lt;T&gt;</code>", version.ReleaseNotesHtml);
        Assert.DoesNotContain("onerror", version.ReleaseNotesHtml);

        var entry = Assert.Single(version.Entries);
        Assert.NotNull(entry.TextHtml);
        Assert.Contains("<a href=\"https://github.com/o/r/pull/1\"", entry.TextHtml);
        Assert.DoesNotContain("onerror", entry.TextHtml);
    }

    [Fact]
    public async Task Rendering_metadata_does_not_touch_the_stored_parsed_notes()
    {
        var note = await SeedCachedPackageAsync();
        var stored = note.ParsedNotesJson;
        var service = new PackageCacheService(
            _unitOfWork, TestRegistries.Create(), _renderer, null!, new CachedLookups(_unitOfWork, new MemoryCache(new MemoryCacheOptions())), null!, NullLogger<PackageCacheService>.Instance);

        await service.GetOrFetchMetadataAsync("nuget", "My.Pkg");

        Assert.Equal(stored, note.ParsedNotesJson);
        Assert.DoesNotContain("TextHtml", note.ParsedNotesJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Entry_html_is_omitted_from_json_when_absent()
    {
        var json = JsonSerializer.Serialize(new List<ReleaseNoteEntry> { new() { Text = "x" } });

        Assert.DoesNotContain("TextHtml", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Blog_post_carries_sanitized_html()
    {
        await _unitOfWork.BlogPosts.AddAsync(new BlogPost
        {
            Title = "t",
            Slug = "my-post",
            Content = "Uses List<T>.\n\n<script>alert(1)</script>\n\n[x](javascript:alert(1))",
            IsPublished = true,
            PublishedAt = DateTime.UtcNow
        });

        var result = await new BlogController(_unitOfWork, _renderer).GetBlogBySlug("my-post");

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        var html = json.RootElement.GetProperty("ContentHtml").GetString()!;
        Assert.Contains("List&lt;T&gt;", html);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Uses List<T>.\n\n<script>alert(1)</script>\n\n[x](javascript:alert(1))", json.RootElement.GetProperty("Content").GetString());
    }

    [Fact]
    public void Admin_preview_uses_the_same_renderer()
    {
        var controller = new AdminBlogController(
            _unitOfWork, null!, new HangfireJobScheduler(new RecordingBackgroundJobClient()), CreateReader(), _renderer, NullLogger<AdminBlogController>.Instance);

        var result = controller.PreviewBlog(new BlogPreviewRequest { Content = "**hi** <img src=x onerror=alert(1)>" });

        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(_renderer.RenderHtml("**hi** <img src=x onerror=alert(1)>"), json.RootElement.GetProperty("html").GetString());
        Assert.DoesNotContain("onerror", json.RootElement.GetProperty("html").GetString()!);
    }

    private async Task<CachedReleaseNote> SeedCachedPackageAsync()
    {
        await _unitOfWork.Registries.AddAsync(new Registry { Value = "nuget" });
        var package = await _unitOfWork.CachedPackages.AddAsync(new CachedPackage
        {
            PackageId = "My.Pkg",
            RegistryId = 1,
            LatestVersion = "1.0.0",
            AllVersionsJson = "[\"1.0.0\"]",
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });

        return await _unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote
        {
            CachedPackageId = package.Id,
            Version = "1.0.0",
            ReleaseNotes = Notes,
            ParsedNotesJson = JsonSerializer.Serialize(new AnalyzerService().ParseReleaseNotes(Notes)),
            IsEmpty = false,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
    }

    private SystemSettingsReader CreateReader() =>
        new(_unitOfWork, new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)), NullLogger<SystemSettingsReader>.Instance);
}
