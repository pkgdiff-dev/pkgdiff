using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.API.Controllers;
using PkgDiff.API.Services;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Jobs;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Infrastructure.Services;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Content;

public class AutoPilotApprovalTests
{
    private const string DefaultReleaseNotes =
        "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx Added a new API and fixed several bugs in the parser module.";

    private const string LlmResponse =
        "## Overview\n\n" +
        "pkg is a small parsing library for structured text. It reads and validates documents. Body <script>x</script> uses List<T>. " +
        "This post covers what changed in pkg 1.0.0.\n\n" +
        "## What's changed\n\n" +
        "- Added a new API for reading documents.\n" +
        "- Fixed several bugs in the parser module.\n\n" +
        "## Upgrade notes\n\n" +
        "- The release notes do not mention breaking changes.";

    private const string WellFormedLlmResponseWithNoise =
        "# 🚀 Amazing pkg Release!!\n\n" +
        "## 🚀 Overview\n\n" +
        "pkg is a small parsing library for structured text documents. It reads and validates documents. " +
        "This post covers the changes that ship in pkg version 1.0.0.\n\n" +
        "## What's changed\n\n" +
        "- Added a new API for reading documents 🔥\n" +
        "- Fixed several bugs in the parser module.\n\n" +
        "## Upgrade notes\n\n" +
        "The release notes do not mention breaking changes.";

    private const string GarbageLlmResponse =
        "# 🚀🔥 pkg RELEASE 🎉\n\n## 🌟 Awesome News 🌟\n\npkg is the best thing ever!";

    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly ReleaseNotesRenderer _renderer = new();
    private readonly RecordingSocialMediaService _social = new();
    private readonly RecordingBackgroundJobClient _jobs = new();

    [Fact]
    public async Task AutoPilot_SavesTheDraftAsMarkdown_AndNeverPostsToSocialMedia()
    {
        await SeedPackageAsync(DefaultReleaseNotes);

        await CreateAutoPilot(new FixedOllamaService(LlmResponse)).RunAutoPilotJobAsync();

        var post = Assert.Single(_unitOfWork.BlogPosts.Items);
        Assert.False(post.IsPublished);
        Assert.Null(post.PublishedAt);
        // Stored as generated; it is made safe when rendered
        Assert.Contains("uses List<T>.", post.Content);
        var html = _renderer.RenderHtml(post.Content);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Body  uses List&lt;T&gt;.", html);
        Assert.Empty(_social.Calls);
        Assert.Empty(_unitOfWork.SocialMediaPosts.Items);
    }

    [Fact]
    public async Task AutoPilot_GarbageLlmOutput_SavesNoDraft()
    {
        await SeedPackageAsync(DefaultReleaseNotes);
        var service = CreateAutoPilot(new FixedOllamaService(GarbageLlmResponse));

        var exception = await Record.ExceptionAsync(() => service.RunAutoPilotJobAsync());

        Assert.Null(exception);
        Assert.Empty(_unitOfWork.BlogPosts.Items);
        Assert.Equal(0, _unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task AutoPilot_WellFormedOutput_SavesDraftWithDeterministicTitleAndOptions()
    {
        await SeedPackageAsync(DefaultReleaseNotes);
        var ollama = new FixedOllamaService(WellFormedLlmResponseWithNoise);

        await CreateAutoPilot(ollama).RunAutoPilotJobAsync();

        var post = Assert.Single(_unitOfWork.BlogPosts.Items);
        Assert.Equal("pkg 1.0.0: what's new", post.Title);
        Assert.StartsWith("pkg-100-whats-new-", post.Slug);
        Assert.False(post.IsPublished);
        Assert.Null(post.PublishedAt);
        Assert.Contains("## Overview", post.Content);
        Assert.Contains("## Upgrade notes", post.Content);
        Assert.DoesNotContain(post.Content.Split('\n'), line => line.StartsWith("# ", StringComparison.Ordinal));
        Assert.DoesNotContain("🚀", post.Content);
        Assert.DoesNotContain("🔥", post.Content);
        Assert.Equal("qwen2:0.5b", post.LlmModelUsed);

        var options = Assert.IsType<OllamaGenerationOptions>(ollama.LastOptions);
        Assert.Equal(0.2, options.Temperature);
        Assert.Equal(0.9, options.TopP);
        Assert.Equal(1.15, options.RepeatPenalty);
        Assert.Equal(4096, options.NumCtx);
        Assert.Equal(600, options.NumPredict);
        Assert.Contains("## Overview", ollama.LastSystemMessage);
        Assert.Contains("## What's changed", ollama.LastSystemMessage);
        Assert.Contains("## Upgrade notes", ollama.LastSystemMessage);
        Assert.Contains("emoji", ollama.LastSystemMessage);
    }

    [Fact]
    public void AutoPilotJob_IsNotRetriedAndNeverRunsTwiceAtOnce()
    {
        var method = typeof(IAutoPilotService).GetMethod(nameof(IAutoPilotService.RunAutoPilotJobAsync))!;

        var retry = Assert.Single(method.GetCustomAttributes(typeof(global::Hangfire.AutomaticRetryAttribute), false));
        Assert.Equal(0, ((global::Hangfire.AutomaticRetryAttribute)retry).Attempts);
        Assert.Single(method.GetCustomAttributes(typeof(global::Hangfire.DisableConcurrentExecutionAttribute), false));
    }

    [Fact]
    public async Task AutoPilot_LongReleaseNotes_AreTruncatedInThePrompt()
    {
        var notes = new System.Text.StringBuilder();
        while (notes.Length < 10000)
            notes.Append("Fixed a bug in the parser module. ");
        notes.Append("TAILMARKER-XYZ");
        await SeedPackageAsync(notes.ToString());
        var ollama = new FixedOllamaService(LlmResponse);

        await CreateAutoPilot(ollama).RunAutoPilotJobAsync();

        Assert.DoesNotContain("TAILMARKER-XYZ", ollama.LastPrompt);
        Assert.Contains("[Release notes truncated]", ollama.LastPrompt);
    }

    [Fact]
    public async Task TogglePublish_Draft_WithAutoPostEnabled_EnqueuesExactlyOneSocialJob()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: false);

        var result = await CreateController().TogglePublish(blog.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.True(blog.IsPublished);
        Assert.NotNull(blog.PublishedAt);
        var job = Assert.Single(_jobs.Jobs);
        Assert.Equal(typeof(BlogSocialPostJob), job.Type);
        Assert.Equal(nameof(BlogSocialPostJob.ExecuteAsync), job.Method.Name);
        Assert.Equal(blog.Id, Assert.IsType<int>(job.Args[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task TogglePublish_Draft_WithAutoPostMissingOrDisabled_PublishesWithoutAJob(string? setting)
    {
        if (setting is not null)
            await SetAutoPostAsync(setting);
        var blog = await AddBlogAsync(isPublished: false);

        await CreateController().TogglePublish(blog.Id);

        Assert.True(blog.IsPublished);
        Assert.Empty(_jobs.Jobs);
    }

    [Fact]
    public async Task TogglePublish_Published_UnpublishesWithoutAJob()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);

        await CreateController().TogglePublish(blog.Id);

        Assert.False(blog.IsPublished);
        Assert.Empty(_jobs.Jobs);
    }

    [Fact]
    public void TriggerAutoPilot_EnqueuesThroughTheInjectedClient()
    {
        CreateController().TriggerAutoPilot();

        var job = Assert.Single(_jobs.Jobs);
        Assert.Equal(typeof(IAutoPilotService), job.Type);
    }

    [Fact]
    public async Task UpdateBlog_StoresTheMarkdownAsWritten_AndItRendersInert()
    {
        var blog = await AddBlogAsync(isPublished: false);

        await CreateController().UpdateBlog(blog.Id, new UpdateBlogRequest { Title = "t", Content = "<img src=x onerror=alert(1)>ok" });

        Assert.Equal("<img src=x onerror=alert(1)>ok", blog.Content);
        var html = _renderer.RenderHtml(blog.Content);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok", html);
    }

    [Fact]
    public async Task SocialJob_SkipsPlatformsThatAlreadySucceeded()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        await _unitOfWork.SocialMediaPosts.AddAsync(new SocialMediaPost
        {
            Platform = SocialMediaPlatform.Twitter,
            Status = SocialMediaPostStatus.Success,
            BlogPostId = blog.Id
        });

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Equal(["LinkedIn"], _social.Calls);
        var linkedIn = Assert.Single(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.LinkedIn);
        Assert.Equal(SocialMediaPostStatus.Success, linkedIn.Status);
        Assert.Equal(blog.Id, linkedIn.BlogPostId);
        Assert.Contains("https://pkgdiff.dev/blog/" + blog.Slug, linkedIn.Content);
        Assert.Contains("#MyPkg", linkedIn.Content);
        Assert.Equal(2, _unitOfWork.SocialMediaPosts.Items.Count);
    }

    [Fact]
    public async Task SocialJob_PostsToBothPlatforms_WhenNothingWasPostedYet()
    {
        await SetAutoPostAsync("TRUE");
        var blog = await AddBlogAsync(isPublished: true);

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Equal(["Twitter", "LinkedIn"], _social.Calls);
        Assert.Equal(2, _unitOfWork.SocialMediaPosts.Items.Count(p => p.Status == SocialMediaPostStatus.Success));
    }

    [Fact]
    public async Task SocialJob_DoesNothing_ForAnUnpublishedPost()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: false);

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Empty(_social.Calls);
        Assert.Empty(_unitOfWork.SocialMediaPosts.Items);
    }

    [Fact]
    public async Task SocialJob_DoesNothing_WhenAutoPostIsMissing()
    {
        var blog = await AddBlogAsync(isPublished: true);

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Empty(_social.Calls);
        Assert.Empty(_unitOfWork.SocialMediaPosts.Items);
    }

    [Fact]
    public async Task SocialJob_DoesNothing_ForAMissingPost()
    {
        await SetAutoPostAsync("true");

        await CreateJob().ExecuteAsync(999);

        Assert.Empty(_social.Calls);
    }

    [Fact]
    public async Task SocialJob_RecordsAFailure_AndThrows()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _social.LinkedInResult = SocialPostResult.Rejected("LinkedIn rejected the post");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateJob().ExecuteAsync(blog.Id));

        Assert.Contains("LinkedIn", ex.Message);
        Assert.DoesNotContain("pkgdiff.dev", ex.Message);
        var failed = Assert.Single(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.LinkedIn);
        Assert.Equal(SocialMediaPostStatus.Failed, failed.Status);
        Assert.Equal("LinkedIn rejected the post", failed.ErrorMessage);
        Assert.Contains(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.Twitter && p.Status == SocialMediaPostStatus.Success);
    }

    [Fact]
    public async Task SocialJob_Retry_OnlyRetriesTheFailedPlatform()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _social.LinkedInResult = SocialPostResult.Rejected("temporary");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateJob().ExecuteAsync(blog.Id));
        _social.LinkedInResult = SocialPostResult.Succeeded("https://linkedin.example/p/1");
        _social.Calls.Clear();

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Equal(["LinkedIn"], _social.Calls);
    }

    // WR-06: an unconfigured platform is skipped, not a failure that makes Hangfire retry
    [Fact]
    public async Task SocialJob_OnlyLinkedInConfigured_SucceedsWithoutTouchingTwitter()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _social.Configured.Remove(SocialMediaPlatform.Twitter);

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Equal(["LinkedIn"], _social.Calls);
        var post = Assert.Single(_unitOfWork.SocialMediaPosts.Items);
        Assert.Equal(SocialMediaPlatform.LinkedIn, post.Platform);
        Assert.Equal(SocialMediaPostStatus.Success, post.Status);
    }

    [Fact]
    public async Task SocialJob_NothingConfigured_SucceedsWithoutRows()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _social.Configured.Clear();

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Empty(_social.Calls);
        Assert.Empty(_unitOfWork.SocialMediaPosts.Items);
    }

    [Fact]
    public async Task DeleteBlog_RemovesTheBlog_ReturnsNoContent()
    {
        var blog = await AddBlogAsync(isPublished: true);
        await _unitOfWork.SocialMediaPosts.AddAsync(new SocialMediaPost
        {
            Platform = SocialMediaPlatform.Twitter,
            Status = SocialMediaPostStatus.Success,
            BlogPostId = blog.Id
        });

        var result = await CreateController().DeleteBlog(blog.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(_unitOfWork.BlogPosts.Items);
    }

    [Fact]
    public async Task DeleteBlog_Missing_ReturnsNotFound()
    {
        var result = await CreateController().DeleteBlog(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteBlog_WhenSaveFails_Returns500WithGenericError()
    {
        var blog = await AddBlogAsync(isPublished: true);
        _unitOfWork.SaveChangesException = new InvalidOperationException(
            "23503: update or delete on table \"BlogPosts\" violates foreign key constraint");

        var result = await CreateController().DeleteBlog(blog.Id);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
        Assert.Contains("\"error\"", json);
        Assert.DoesNotContain("23503", json);
    }

    [Fact]
    public async Task SocialJob_MissingBlog_DoesNotThrow()
    {
        await SetAutoPostAsync("true");

        var exception = await Record.ExceptionAsync(() => CreateJob().ExecuteAsync(999));

        Assert.Null(exception);
        Assert.Empty(_social.Calls);
    }

    [Fact]
    public async Task SocialJob_BlogDeletedAfterPublish_DoesNotThrowOrPost()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: false);
        await CreateController().TogglePublish(blog.Id);
        await _unitOfWork.BlogPosts.DeleteAsync(blog);

        var exception = await Record.ExceptionAsync(() => CreateJob().ExecuteAsync(blog.Id));

        Assert.Null(exception);
        Assert.Empty(_social.Calls);
        Assert.Empty(_unitOfWork.SocialMediaPosts.Items);
    }

    // WR-06: the platform accepted the post but recording the outcome failed; the retry must not post again
    [Fact]
    public async Task SocialJob_RetryAfterUnrecordedSuccess_DoesNotPostTwice()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _unitOfWork.FailOnSaveChangesCall = _unitOfWork.SaveChangesCalls + 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateJob().ExecuteAsync(blog.Id));
        Assert.Equal(["Twitter"], _social.Calls);

        // The outcome update was never saved, so the database still holds the row as Pending
        var twitter = Assert.Single(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.Twitter);
        twitter.Status = SocialMediaPostStatus.Pending;
        twitter.PostUrl = null;
        _unitOfWork.FailOnSaveChangesCall = null;
        _social.Calls.Clear();

        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Equal(["LinkedIn"], _social.Calls);
        Assert.Single(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.Twitter);
        Assert.Equal(SocialMediaPostStatus.Pending, twitter.Status);
    }

    // WR-03: a timeout or 5xx may already have published the post, so it stays Pending and is never retried
    [Fact]
    public async Task SocialJob_UnknownOutcome_StaysPending_DoesNotThrow_AndIsNotRepostedLater()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _social.TwitterResult = SocialPostResult.Unknown("The Twitter request did not complete");

        await CreateJob().ExecuteAsync(blog.Id);

        var twitter = Assert.Single(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.Twitter);
        Assert.Equal(SocialMediaPostStatus.Pending, twitter.Status);
        Assert.Equal("The Twitter request did not complete", twitter.ErrorMessage);
        Assert.Contains(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.LinkedIn && p.Status == SocialMediaPostStatus.Success);

        _social.Calls.Clear();
        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Empty(_social.Calls);
        Assert.Single(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.Twitter);
    }

    [Fact]
    public async Task SocialJob_UnknownAndRejected_ThrowsOnlyForTheRejectedPlatform_AndRetriesOnlyIt()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _social.TwitterResult = SocialPostResult.Unknown("timeout");
        _social.LinkedInResult = SocialPostResult.Rejected("LinkedIn API returned Forbidden");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateJob().ExecuteAsync(blog.Id));
        Assert.Contains("LinkedIn", ex.Message);
        Assert.DoesNotContain("Twitter", ex.Message);

        _social.LinkedInResult = SocialPostResult.Succeeded("https://linkedin.example/p/1");
        _social.Calls.Clear();
        await CreateJob().ExecuteAsync(blog.Id);

        Assert.Equal(["LinkedIn"], _social.Calls);
        Assert.Single(_unitOfWork.SocialMediaPosts.Items, p => p.Platform == SocialMediaPlatform.Twitter);
    }

    [Fact]
    public async Task SocialJob_PostIsRecordedAsPendingBeforeThePlatformIsCalled()
    {
        await SetAutoPostAsync("true");
        var blog = await AddBlogAsync(isPublished: true);
        _social.Configured.Remove(SocialMediaPlatform.LinkedIn);
        var saveCallsBefore = _unitOfWork.SaveChangesCalls;

        await CreateJob().ExecuteAsync(blog.Id);

        // One save for the Pending row before the call, one for the outcome after it
        Assert.Equal(saveCallsBefore + 2, _unitOfWork.SaveChangesCalls);
        Assert.Equal(SocialMediaPostStatus.Success, Assert.Single(_unitOfWork.SocialMediaPosts.Items).Status);
    }

    private async Task SetAutoPostAsync(string value)
    {
        await _unitOfWork.SystemSettings.AddAsync(new SystemSettings { Key = SettingKeys.SocialMediaAutoPostEnabled, Value = value });
    }

    private async Task<BlogPost> AddBlogAsync(bool isPublished)
    {
        return await _unitOfWork.BlogPosts.AddAsync(new BlogPost
        {
            Title = "My.Pkg 2.0",
            Slug = "my-pkg-2-0-abcde",
            Content = "content",
            PackageId = "My.Pkg",
            Version = "2.0.0",
            IsPublished = isPublished,
            PublishedAt = isPublished ? DateTime.UtcNow : null
        });
    }

    private async Task SeedPackageAsync(string releaseNotes)
    {
        var registry = new Registry { Id = 1, Value = "npm" };
        await _unitOfWork.CachedPackages.AddAsync(new CachedPackage
        {
            PackageId = "pkg",
            LatestVersion = "1.0.0",
            Registry = registry,
            CachedAt = DateTime.UtcNow
        });
        await _unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote
        {
            CachedPackageId = 1,
            Version = "1.0.0",
            ReleaseNotes = releaseNotes,
            CachedAt = DateTime.UtcNow
        });
    }

    private AutoPilotService CreateAutoPilot(IOllamaService ollama) =>
        new(
            _unitOfWork,
            ollama,
            NullLogger<AutoPilotService>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection().Build());

    private SystemSettingsReader CreateReader() =>
        new(_unitOfWork, new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)), NullLogger<SystemSettingsReader>.Instance);

    private AdminBlogController CreateController() =>
        new(_unitOfWork, new ThrowingAutoPilotService(), new HangfireJobScheduler(_jobs), CreateReader(), _renderer, NullLogger<AdminBlogController>.Instance);

    private BlogSocialPostJob CreateJob() =>
        new(_unitOfWork, _social, CreateReader(), NullLogger<BlogSocialPostJob>.Instance);

    private sealed class FixedOllamaService : IOllamaService
    {
        private readonly string _response;

        public FixedOllamaService(string response)
        {
            _response = response;
        }

        public string LastPrompt { get; private set; } = string.Empty;

        public string LastSystemMessage { get; private set; } = string.Empty;

        public string LastModel { get; private set; } = string.Empty;

        public OllamaGenerationOptions? LastOptions { get; private set; }

        public Task<string> GenerateTextAsync(
            string prompt,
            string systemMessage = "",
            string model = "phi3",
            OllamaGenerationOptions? options = null)
        {
            LastPrompt = prompt;
            LastSystemMessage = systemMessage;
            LastModel = model;
            LastOptions = options;
            return Task.FromResult(_response);
        }
    }

    private sealed class ThrowingAutoPilotService : IAutoPilotService
    {
        public Task RunAutoPilotJobAsync() => throw new InvalidOperationException("must be enqueued, not run inline");
    }

    private sealed class RecordingSocialMediaService : ISocialMediaService
    {
        public List<string> Calls { get; } = new();

        public SocialPostResult TwitterResult { get; set; } = SocialPostResult.Succeeded("https://x.example/status/1");

        public SocialPostResult LinkedInResult { get; set; } = SocialPostResult.Succeeded("https://linkedin.example/p/1");

        public Task<SocialPostResult> PostToTwitterAsync(string content)
        {
            Calls.Add("Twitter");
            return Task.FromResult(TwitterResult);
        }

        public Task<SocialPostResult> PostToLinkedInAsync(string content)
        {
            Calls.Add("LinkedIn");
            return Task.FromResult(LinkedInResult);
        }

        public HashSet<SocialMediaPlatform> Configured { get; } = [SocialMediaPlatform.Twitter, SocialMediaPlatform.LinkedIn];

        public Task<bool> IsConfiguredAsync(SocialMediaPlatform platform) => Task.FromResult(Configured.Contains(platform));

        public Task<(string AuthUrl, string State)?> GetLinkedInAuthUrlAsync(string redirectUri, string userId) =>
            throw new NotSupportedException();

        public Task<(bool Success, string? ErrorMessage)> HandleLinkedInCallbackAsync(string code, string state, string userId) =>
            throw new NotSupportedException();
    }
}
