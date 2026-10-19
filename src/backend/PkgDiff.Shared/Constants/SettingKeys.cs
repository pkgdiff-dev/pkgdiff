namespace PkgDiff.Shared.Constants;

public static class SettingKeys
{
    public const string CategoryGeneral = "General";
    public const string CategoryAutoPilot = "AutoPilot";
    public const string CategorySocialMedia = "Social Media";
    public const string CategoryLinkedIn = "Social Media: LinkedIn";
    public const string CategoryTwitter = "Social Media: Twitter";
    public const string CategorySecurity = "Security";

    public const string CacheEnabled = "CacheEnabled";
    public const string CacheTtlMinutes = "CacheTTLMinutes";
    public const string MaxVersionsToAnalyze = "MaxVersionsToAnalyze";
    public const string MaxConsecutiveEmptyVersions = "MaxConsecutiveEmptyVersions";
    public const string ScrapingTimeout = "ScrapingTimeout";
    public const string HangfireWorkerCount = "Hangfire.WorkerCount";

    public const string AutoPilotEnabled = "AutoPilot:Enabled";
    public const string AutoPilotLlmModel = "AutoPilot:LlmModel";
    public const string AutoPilotDailyFrequency = "AutoPilot.DailyFrequency";

    public const string SocialMediaAutoPostEnabled = "SocialMedia:AutoPostEnabled";

    public const string LinkedInClientId = "SocialMedia:LinkedIn:ClientId";
    public const string LinkedInClientSecret = "SocialMedia:LinkedIn:ClientSecret";
    public const string LinkedInAccessToken = "SocialMedia:LinkedIn:AccessToken";
    public const string LinkedInAuthorUrn = "SocialMedia:LinkedIn:AuthorURN";

    public const string TwitterApiKey = "SocialMedia:Twitter:ApiKey";
    public const string TwitterApiKeySecret = "SocialMedia:Twitter:ApiKeySecret";
    public const string TwitterAccessToken = "SocialMedia:Twitter:AccessToken";
    public const string TwitterAccessTokenSecret = "SocialMedia:Twitter:AccessTokenSecret";

    public const string SecurityOsvEnabled = "Security:Osv:Enabled";
    public const string SecurityDepsDevEnabled = "Security:DepsDev:Enabled";
    public const string SecurityCacheHours = "Security:CacheHours";
    public const string SecuritySocketEnabled = "Security:Socket:Enabled";
    public const string SecuritySocketApiKey = "Security:Socket:ApiKey";
    public const string SecuritySocketOrg = "Security:Socket:Org";

    private const string TypeBool = "Bool";
    private const string TypeInt = "Int";
    private const string TypeString = "String";

    public static IReadOnlyList<SettingDefinition> All { get; } = new[]
    {
        new SettingDefinition(CacheEnabled, "true", "Enable/disable caching", TypeBool, CategoryGeneral, false),
        new SettingDefinition(CacheTtlMinutes, "60", "Cache TTL in minutes", TypeInt, CategoryGeneral, false),
        new SettingDefinition(MaxVersionsToAnalyze, "50", "Maximum versions to analyze initially", TypeInt, CategoryGeneral, false),
        new SettingDefinition(MaxConsecutiveEmptyVersions, "10", "Stop after N consecutive empty release notes", TypeInt, CategoryGeneral, false),
        new SettingDefinition(ScrapingTimeout, "30", "Scraping timeout in seconds", TypeInt, CategoryGeneral, false),
        new SettingDefinition(HangfireWorkerCount, "10", "Number of Hangfire background workers (informational; the server does not read this yet)", TypeInt, CategoryGeneral, false),

        new SettingDefinition(AutoPilotEnabled, "true", "Enable/disable AutoPilot blog generation job", TypeBool, CategoryAutoPilot, false),
        new SettingDefinition(AutoPilotLlmModel, "qwen2:0.5b", "Ollama model name used by AutoPilot for blog generation", TypeString, CategoryAutoPilot, false),
        new SettingDefinition(AutoPilotDailyFrequency, "3", "How many times per day the AutoPilot blog job runs (1, 2, 3, 4, 6, 8, 12 or 24)", TypeInt, CategoryAutoPilot, false),

        new SettingDefinition(SocialMediaAutoPostEnabled, "false", "Post to Twitter/LinkedIn when an admin publishes an AutoPilot blog post", TypeBool, CategorySocialMedia, false),

        new SettingDefinition(LinkedInClientId, "", "LinkedIn App Client ID (from LinkedIn Developer Portal)", TypeString, CategoryLinkedIn, false),
        new SettingDefinition(LinkedInClientSecret, "", "LinkedIn App Client Secret (from LinkedIn Developer Portal)", TypeString, CategoryLinkedIn, true),
        new SettingDefinition(LinkedInAccessToken, "", "LinkedIn OAuth Access Token (auto-generated via Connect LinkedIn button)", TypeString, CategoryLinkedIn, true),
        new SettingDefinition(LinkedInAuthorUrn, "", "LinkedIn Author URN for posting (e.g. urn:li:organization:123456)", TypeString, CategoryLinkedIn, false),

        new SettingDefinition(TwitterApiKey, "", "Twitter API Key (Consumer Key)", TypeString, CategoryTwitter, true),
        new SettingDefinition(TwitterApiKeySecret, "", "Twitter API Key Secret (Consumer Secret)", TypeString, CategoryTwitter, true),
        new SettingDefinition(TwitterAccessToken, "", "Twitter Access Token", TypeString, CategoryTwitter, true),
        new SettingDefinition(TwitterAccessTokenSecret, "", "Twitter Access Token Secret", TypeString, CategoryTwitter, true),

        new SettingDefinition(SecurityOsvEnabled, "true", "Show known vulnerabilities from OSV.dev for every package", TypeBool, CategorySecurity, false),
        new SettingDefinition(SecurityDepsDevEnabled, "true", "Show project health (OpenSSF Scorecard, licenses, provenance) from deps.dev and the Scorecard API", TypeBool, CategorySecurity, false),
        new SettingDefinition(SecurityCacheHours, "24", "Hours security data is served before a background job refreshes it (24-72)", TypeInt, CategorySecurity, false),
        new SettingDefinition(SecuritySocketEnabled, "false", "Show Socket.dev behaviour alerts. Leave off until Socket has allowed public display (docs/ops/socket-permission.md)", TypeBool, CategorySecurity, false),
        new SettingDefinition(SecuritySocketApiKey, "", "Socket.dev API token with the packages:list scope", TypeString, CategorySecurity, true),
        new SettingDefinition(SecuritySocketOrg, "", "Socket.dev organization slug the token belongs to", TypeString, CategorySecurity, false)
    };

    private static readonly Dictionary<string, SettingDefinition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static SettingDefinition? Find(string key) =>
        ByKey.TryGetValue(key, out var definition) ? definition : null;
}
