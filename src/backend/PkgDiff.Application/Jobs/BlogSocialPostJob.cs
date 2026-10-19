using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Jobs;

public class BlogSocialPostJob
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISocialMediaService _socialMediaService;
    private readonly ISystemSettingsReader _settingsReader;
    private readonly ILogger<BlogSocialPostJob> _logger;

    public BlogSocialPostJob(
        IUnitOfWork unitOfWork,
        ISocialMediaService socialMediaService,
        ISystemSettingsReader settingsReader,
        ILogger<BlogSocialPostJob> logger)
    {
        _unitOfWork = unitOfWork;
        _socialMediaService = socialMediaService;
        _settingsReader = settingsReader;
        _logger = logger;
    }

    // Serialized so two queued runs for the same post cannot both see "not yet posted"
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(int blogPostId)
    {
        var blogPost = await _unitOfWork.BlogPosts.GetByIdAsync(blogPostId);
        if (blogPost == null || !blogPost.IsPublished)
        {
            _logger.LogInformation("Blog post {BlogPostId} is missing or not published, skipping social posting", blogPostId);
            return;
        }

        // A missing setting means disabled: nothing is posted unless an admin explicitly turned it on
        var autoPostEnabled = await _settingsReader.GetAsync(SettingKeys.SocialMediaAutoPostEnabled);
        if (!string.Equals(autoPostEnabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Social media auto-posting is disabled, skipping blog post {BlogPostId}", blogPostId);
            return;
        }

        var previousPosts = (await _unitOfWork.SocialMediaPosts.FindAsync(p =>
                p.BlogPostId == blogPostId
                && (p.Status == SocialMediaPostStatus.Success || p.Status == SocialMediaPostStatus.Pending)))
            .ToList();

        var message = BuildShareMessage(blogPost);
        var failedPlatforms = new List<SocialMediaPlatform>();

        foreach (var platform in new[] { SocialMediaPlatform.Twitter, SocialMediaPlatform.LinkedIn })
        {
            if (previousPosts.Any(p => p.Platform == platform && p.Status == SocialMediaPostStatus.Success))
                continue;

            // A Pending row means an earlier attempt reached the platform but its outcome was never recorded;
            // posting again could publish a duplicate, so it is left for an admin to check
            if (previousPosts.Any(p => p.Platform == platform && p.Status == SocialMediaPostStatus.Pending))
            {
                _logger.LogWarning(
                    "Blog post {BlogPostId} has an unconfirmed {Platform} post from an earlier attempt, not posting again",
                    blogPostId,
                    platform);
                continue;
            }

            // Not configured is a deliberate choice, not a failure: skip without failing or retrying the job
            if (!await _socialMediaService.IsConfiguredAsync(platform))
            {
                _logger.LogInformation("{Platform} is not configured, skipping blog post {BlogPostId}", platform, blogPostId);
                continue;
            }

            var post = await _unitOfWork.SocialMediaPosts.AddAsync(new SocialMediaPost
            {
                Platform = platform,
                Content = message,
                Status = SocialMediaPostStatus.Pending,
                BlogPostId = blogPostId
            });
            await _unitOfWork.SaveChangesAsync();

            var result = platform == SocialMediaPlatform.Twitter
                ? await _socialMediaService.PostToTwitterAsync(message)
                : await _socialMediaService.PostToLinkedInAsync(message);

            // An unknown outcome stays Pending so no retry can publish a duplicate; an admin reconciles it
            post.Status = result.Outcome switch
            {
                SocialPostOutcome.Succeeded => SocialMediaPostStatus.Success,
                SocialPostOutcome.Rejected => SocialMediaPostStatus.Failed,
                _ => SocialMediaPostStatus.Pending
            };
            post.PostUrl = result.PostUrl;
            post.ErrorMessage = result.ErrorMessage;
            await _unitOfWork.SocialMediaPosts.UpdateAsync(post);
            await _unitOfWork.SaveChangesAsync();

            if (result.Outcome == SocialPostOutcome.Unknown)
            {
                _logger.LogWarning(
                    "The outcome of the {Platform} post for blog post {BlogPostId} is unknown; it is left Pending and not retried",
                    platform,
                    blogPostId);
            }
            else if (result.Outcome == SocialPostOutcome.Rejected)
            {
                failedPlatforms.Add(platform);
            }
        }

        if (failedPlatforms.Count > 0)
        {
            // Throwing lets Hangfire record the failure and retry; succeeded platforms are skipped on retry
            throw new InvalidOperationException(
                $"Social posting failed for blog post {blogPostId} on: {string.Join(", ", failedPlatforms)}");
        }

        _logger.LogInformation("Social media posting completed for blog post {BlogPostId}", blogPostId);
    }

    private static string BuildShareMessage(BlogPost blogPost)
    {
        var blogUrl = $"https://pkgdiff.dev/blog/{blogPost.Slug}";
        var hashtag = Regex.Replace(blogPost.PackageId, @"[^a-zA-Z0-9]", "");
        return $"🚀 We just published a new analysis for {blogPost.PackageId} v{blogPost.Version}!\n\nCheck out the release notes, breaking changes, and full overview on our blog:\n\n{blogUrl}\n\n#openSource #programming #{hashtag}";
    }
}
