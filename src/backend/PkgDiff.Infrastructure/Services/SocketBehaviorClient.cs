using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

// Socket.dev behaviour analysis for npm, PyPI and NuGet; off unless enabled with a token and org.
// The free tier allows about five batch calls an hour, so calls are batched and capped.
public partial class SocketBehaviorClient : IBehaviorClient
{
    private const int MaxVersionsPerCall = 20;
    private const int MaxCallsPerHour = 4;

    private static readonly Queue<DateTime> RecentCalls = new();
    private static readonly object CallsLock = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ISystemSettingsReader _settings;
    private readonly ILogger<SocketBehaviorClient> _logger;

    public SocketBehaviorClient(IHttpClientFactory httpClientFactory, ISystemSettingsReader settings, ILogger<SocketBehaviorClient> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientNames.Socket);
        _settings = settings;
        _logger = logger;
    }

    public async Task<Dictionary<string, BehaviorReport>> GetReportsAsync(
        string registry,
        string packageId,
        IReadOnlyList<string> versions,
        CancellationToken cancellationToken = default)
    {
        var reports = new Dictionary<string, BehaviorReport>(StringComparer.Ordinal);
        var type = PurlType(registry);
        if (type is null || versions.Count == 0 || !await IsEnabledAsync())
            return reports;

        var token = await _settings.GetAsync(SettingKeys.SecuritySocketApiKey);
        var org = await _settings.GetAsync(SettingKeys.SecuritySocketOrg);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(org))
            return reports;

        if (!TryTakeCall())
        {
            _logger.LogInformation("Socket call for {PackageId} skipped: the hourly budget is used up", packageId);
            return reports;
        }

        var byPurl = versions.Take(MaxVersionsPerCall).ToDictionary(v => Purl(type, registry, packageId, v), v => v, StringComparer.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v0/orgs/{Uri.EscapeDataString(org.Trim())}/purl?alerts=true")
        {
            Content = JsonContent.Create(new { components = byPurl.Keys.Select(purl => new { purl }) }, options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // The answer is NDJSON: one package per line
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            JsonElement item;
            try
            {
                item = JsonSerializer.Deserialize<JsonElement>(line, Json);
            }
            catch (JsonException)
            {
                continue;
            }
            var version = Text(item, "version");
            if (version is null || !versions.Contains(version))
                continue;

            reports[version] = new BehaviorReport
            {
                OverallScore = Score(item, "overall"),
                SupplyChainScore = Score(item, "supplyChain"),
                Alerts = Alerts(item),
                Url = $"https://socket.dev/{type}/package/{Uri.EscapeDataString(packageId)}/overview/{Uri.EscapeDataString(version)}"
            };
        }
        return reports;
    }

    private async Task<bool> IsEnabledAsync() =>
        bool.TryParse(await _settings.GetAsync(SettingKeys.SecuritySocketEnabled), out var enabled) && enabled;

    private static bool TryTakeCall()
    {
        lock (CallsLock)
        {
            var hourAgo = DateTime.UtcNow.AddHours(-1);
            while (RecentCalls.Count > 0 && RecentCalls.Peek() < hourAgo)
                RecentCalls.Dequeue();
            if (RecentCalls.Count >= MaxCallsPerHour)
                return false;
            RecentCalls.Enqueue(DateTime.UtcNow);
            return true;
        }
    }

    private static List<BehaviorAlert> Alerts(JsonElement item)
    {
        if (!item.TryGetProperty("alerts", out var alerts) || alerts.ValueKind != JsonValueKind.Array)
            return new List<BehaviorAlert>();
        return alerts.EnumerateArray()
            .Select(alert => new BehaviorAlert
            {
                Type = Text(alert, "type") ?? "unknown",
                Severity = Text(alert, "severity") switch
                {
                    "critical" => AdvisorySeverity.Critical,
                    "high" => AdvisorySeverity.High,
                    "middle" or "medium" => AdvisorySeverity.Medium,
                    "low" => AdvisorySeverity.Low,
                    _ => AdvisorySeverity.Unknown
                }
            })
            .GroupBy(alert => alert.Type, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    private static double? Score(JsonElement item, string name) =>
        item.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Object
        && score.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? Math.Round(value.GetDouble(), 2)
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? PurlType(string registry) => RegistryNames.Canonical(registry) switch
    {
        RegistryNames.Npm => "npm",
        RegistryNames.PyPI => "pypi",
        RegistryNames.NuGet => "nuget",
        _ => null
    };

    // pkg:npm/%40scope/name@1.0.0, pkg:pypi/normalized-name@1.0, pkg:nuget/Id@1.0.0
    private static string Purl(string type, string registry, string packageId, string version)
    {
        var name = RegistryNames.Canonical(registry) switch
        {
            RegistryNames.Npm when packageId.StartsWith('@') && packageId.Contains('/') =>
                "%40" + Uri.EscapeDataString(packageId[1..packageId.IndexOf('/')]) + "/" + Uri.EscapeDataString(packageId[(packageId.IndexOf('/') + 1)..]),
            RegistryNames.PyPI => Uri.EscapeDataString(PyPINormalization().Replace(packageId.ToLowerInvariant(), "-")),
            _ => Uri.EscapeDataString(packageId)
        };
        return new StringBuilder("pkg:").Append(type).Append('/').Append(name).Append('@').Append(Uri.EscapeDataString(version)).ToString();
    }

    [GeneratedRegex(@"[-_.]+")]
    private static partial Regex PyPINormalization();
}
