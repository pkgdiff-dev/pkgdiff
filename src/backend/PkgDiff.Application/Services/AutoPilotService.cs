using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using System.Text.RegularExpressions;

namespace PkgDiff.Application.Services;

public class AutoPilotService : IAutoPilotService
{
    private const int MaxReleaseNotesChars = 6000;
    private const string ReleaseNotesTruncatedMarker = "[Release notes truncated]";

    // Low temperature keeps small models close to the notes; small budgets suit a slow CPU-only host
    private static readonly OllamaGenerationOptions BlogGenerationOptions = new(
        Temperature: 0.2,
        TopP: 0.9,
        RepeatPenalty: 1.15,
        NumCtx: 4096,
        NumPredict: 600);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IOllamaService _ollamaService;
    private readonly ILogger<AutoPilotService> _logger;
    private readonly string _defaultLlmModel;

    public AutoPilotService(
        IUnitOfWork unitOfWork,
        IOllamaService ollamaService,
        ILogger<AutoPilotService> logger,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _ollamaService = ollamaService;
        _logger = logger;
        _defaultLlmModel = configuration["AutoPilot:LlmModel"] ?? "qwen2:0.5b";
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAutoPilotJobAsync()
    {
        var enabledSetting = (await _unitOfWork.SystemSettings.FindAsync(s => s.Key == "AutoPilot:Enabled")).FirstOrDefault();
        if (enabledSetting != null && enabledSetting.Value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("AutoPilot is disabled via system settings. Skipping job.");
            return;
        }

        var modelSetting = (await _unitOfWork.SystemSettings.FindAsync(s => s.Key == "AutoPilot:LlmModel")).FirstOrDefault();
        var llmModel = modelSetting?.Value ?? _defaultLlmModel;

        _logger.LogInformation("AutoPilot task started with model: {Model}", llmModel);

        try
        {
            var (targetPackage, latestReleaseNote) = await SelectInterestingPackageAsync();
            if (targetPackage == null || latestReleaseNote == null)
            {
                _logger.LogInformation("AutoPilot: No packages with meaningful release notes found to write about right now.");
                return;
            }

            string packageNotes = latestReleaseNote.ReleaseNotes!;
            string packageDescription = targetPackage.Description ?? "A software package.";
            string version = latestReleaseNote.Version ?? targetPackage.LatestVersion ?? "Unknown";

            _logger.LogInformation("AutoPilot: Generating blog post for {PackageId} v{Version}", targetPackage.PackageId, version);

            var systemPrompt = BuildSystemPrompt(targetPackage.PackageId, version);
            var userPrompt = $"Package: {targetPackage.PackageId}\n" +
                             $"Version: {version}\n" +
                             $"Description: {packageDescription}\n" +
                             $"Release notes:\n{TruncateReleaseNotes(packageNotes)}\n\n" +
                             $"Write the post now. Start with the line {AutoPilotQualityGate.OverviewHeading}";

            var llmResponse = await _ollamaService.GenerateTextAsync(userPrompt, systemPrompt, llmModel, BlogGenerationOptions);

            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                _logger.LogWarning("AutoPilot: LLM returned an empty response. Aborting blog creation.");
                return;
            }

            var cleaned = AutoPilotOutputCleaner.Clean(llmResponse, packageNotes);
            if (!AutoPilotQualityGate.Passes(cleaned, targetPackage.PackageId, out var reason))
            {
                // Returning instead of throwing: a Hangfire retry would only replay the same bad model output
                var modelHeadings = llmResponse.Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith('#') || l.StartsWith("**", StringComparison.Ordinal))
                    .Select(l => l.Length > 80 ? l[..80] : l)
                    .Take(10);
                _logger.LogWarning(
                    "AutoPilot: Rejected generated post for {PackageId} {Version} from model {Model}: {Reason}. Model headings: {Headings}",
                    targetPackage.PackageId, version, llmModel, reason, string.Join(" | ", modelHeadings));
                return;
            }

            var post = BuildDraftPost(cleaned, targetPackage, latestReleaseNote, version, llmModel);

            await _unitOfWork.BlogPosts.AddAsync(post);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("AutoPilot: Created draft blog post for {PackageId}: {Slug}", targetPackage.PackageId, post.Slug);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during AutoPilot job execution.");
            throw;
        }
    }

    private async Task<(CachedPackage? Package, CachedReleaseNote? ReleaseNote)> SelectInterestingPackageAsync()
    {
        var recentPackages = await _unitOfWork.CachedPackages.GetTopDescendingWithIncludeAsync(
            p => p.CachedAt, p => p.Registry, 50);

        var allBlogs = await _unitOfWork.ToListAsync(_unitOfWork.BlogPosts.Query()
            .Select(b => new { b.PackageId, b.Version, b.CachedReleaseNoteId }));
        var bloggedVersionKeys = allBlogs
            .Select(b => $"{b.PackageId}::{b.Version}")
            .ToHashSet();

        var bloggedReleaseNoteIds = allBlogs
            .Where(b => b.CachedReleaseNoteId.HasValue)
            .Select(b => b.CachedReleaseNoteId!.Value)
            .ToHashSet();

        foreach (var package in recentPackages)
        {
            var releaseNote = await GetLatestReleaseNote(package.Id);
            
            if (releaseNote != null && bloggedReleaseNoteIds.Contains(releaseNote.Id))
            {
                continue;
            }

            string versionToCheck = releaseNote?.Version ?? package.LatestVersion ?? "Unknown";
            
            if (bloggedVersionKeys.Contains($"{package.PackageId}::{versionToCheck}") || 
                bloggedVersionKeys.Contains($"{package.PackageId}::{package.LatestVersion}"))
            {
                continue;
            }

            if (releaseNote != null && !string.IsNullOrWhiteSpace(releaseNote.ReleaseNotes) && releaseNote.ReleaseNotes.Length >= 80)
            {
                return (package, releaseNote);
            }
            _logger.LogDebug("AutoPilot: Skipping {Package} — no meaningful release notes.", package.PackageId);
        }

        return (null, null);
    }

    private async Task<CachedReleaseNote?> GetLatestReleaseNote(int cachedPackageId)
    {
        var latest = await _unitOfWork.ToListAsync(_unitOfWork.CachedReleaseNotes.Query()
            .Where(r => r.CachedPackageId == cachedPackageId)
            .OrderByDescending(n => n.PublishedDate ?? n.CachedAt)
            .ThenByDescending(n => n.Id)
            .Take(1));
        return latest.FirstOrDefault();
    }

    private static string BuildSystemPrompt(string packageId, string version) =>
        $"You are a technical writer summarizing the release notes of the software package '{packageId}' version {version} for a developer changelog blog. " +
        "Write plain Markdown with exactly three H2 sections, in this order and with these exact headings: " +
        $"'{AutoPilotQualityGate.OverviewHeading}', '{AutoPilotQualityGate.WhatsChangedHeading}', '{AutoPilotQualityGate.UpgradeNotesHeading}'. Use no other headings. " +
        "Do not write an H1 title line; the title is added separately. " +
        $"Overview: one to three sentences on what {packageId} is, based on the description, and that this post covers version {version}. " +
        "What's changed: bullet points, each restating one item from the release notes. " +
        "Upgrade notes: bullet points for breaking changes, deprecations, required runtime versions or migration steps, only if the release notes state them; " +
        "otherwise write exactly: The release notes do not mention breaking changes. " +
        "Use only facts present in the provided release notes and description. Never invent features, versions, requirements, APIs or examples. " +
        "No speculation and no marketing tone. Do not use emoji or decorative symbols. " +
        "Do not write code blocks unless the same code appears in the release notes, and then copy it verbatim. " +
        "State each point once; never repeat sections or sentences. " +
        "Output only the post, with no preamble and no closing remarks.";

    private static string TruncateReleaseNotes(string notes)
    {
        if (notes.Length <= MaxReleaseNotesChars)
            return notes;

        var cut = MaxReleaseNotesChars;
        if (char.IsHighSurrogate(notes[cut - 1]))
            cut--;

        return notes[..cut] + "\n\n" + ReleaseNotesTruncatedMarker;
    }

    private BlogPost BuildDraftPost(string cleanedMarkdown, CachedPackage package, CachedReleaseNote releaseNote, string version, string llmModel)
    {
        var title = $"{package.PackageId} {version}: what's new";

        return new BlogPost
        {
            Title = title,
            Slug = GenerateSlug(title),
            Content = StoredMarkdown.Normalize(cleanedMarkdown),
            PackageId = package.PackageId,
            Version = version,
            Registry = package.Registry?.Value ?? "nuget",
            // LLM output can carry prompt-injected content, so it stays a draft until an admin publishes it
            IsPublished = false,
            CreatedAt = DateTime.UtcNow,
            PublishedAt = null,
            LlmModelUsed = llmModel,
            CachedReleaseNoteId = releaseNote.Id
        };
    }

    private string GenerateSlug(string phrase)
    {
        string str = Regex.Replace(phrase.ToLowerInvariant(), @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"[\s-]+", " ").Trim();
        str = Regex.Replace(str, @"\s", "-");
        
        str = $"{str}-{Guid.NewGuid().ToString().Substring(0, 5)}";
        return str;
    }
}
