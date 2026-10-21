using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Security;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

// OSV.dev: one batch query names the advisories per version, then each advisory is read once
public class OsvClient : IOsvClient
{
    private const int BatchSize = 1000;
    private const int MaxParallelDetails = 8;
    private const int MaxSummaryLength = 300;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<OsvClient> _logger;

    public OsvClient(IHttpClientFactory httpClientFactory, ILogger<OsvClient> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientNames.Osv);
        _logger = logger;
    }

    public async Task<OsvReport> QueryAsync(string registry, string packageId, IReadOnlyList<string> versions, CancellationToken cancellationToken = default)
    {
        var ecosystem = EcosystemOf(registry);
        var versionAdvisories = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (ecosystem is null || versions.Count == 0)
            return new OsvReport(versionAdvisories, new List<SecurityAdvisory>());

        foreach (var batch in versions.Chunk(BatchSize))
        {
            var request = new BatchRequest(batch
                .Select(v => new Query(new PackageRef(packageId, ecosystem), v))
                .ToList());
            using var response = await _http.PostAsJsonAsync("v1/querybatch", request, Json, cancellationToken);
            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync<BatchResponse>(Json, cancellationToken);
            var results = answer?.Results ?? new List<BatchResult>();

            for (var i = 0; i < batch.Length; i++)
            {
                var ids = i < results.Count
                    ? (results[i].Vulns ?? new List<VulnRef>()).Select(v => v.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList()
                    : new List<string>();
                versionAdvisories[batch[i]] = ids;
            }
        }

        var uniqueIds = versionAdvisories.Values.SelectMany(ids => ids).Distinct(StringComparer.Ordinal).ToList();
        var advisories = new List<SecurityAdvisory>();
        using var gate = new SemaphoreSlim(MaxParallelDetails);
        var details = await Task.WhenAll(uniqueIds.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await ReadAdvisoryAsync(id, packageId, ecosystem, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));
        advisories.AddRange(details.OfType<SecurityAdvisory>());

        return new OsvReport(versionAdvisories, advisories.OrderBy(a => SeverityRank(a.Severity)).ThenBy(a => a.Id, StringComparer.Ordinal).ToList());
    }

    public async Task<IReadOnlyList<OsvPackageResult>> QueryPackagesAsync(
        string registry,
        IReadOnlyList<(string Name, string Version)> packages,
        CancellationToken cancellationToken = default)
    {
        var ecosystem = EcosystemOf(registry);
        if (ecosystem is null || packages.Count == 0)
            return packages.Select(p => new OsvPackageResult(p.Name, p.Version, new List<SecurityAdvisory>())).ToList();

        var idsPerPackage = new List<List<string>>(packages.Count);
        foreach (var batch in packages.Chunk(BatchSize))
        {
            var request = new BatchRequest(batch
                .Select(p => new Query(new PackageRef(p.Name, ecosystem), p.Version))
                .ToList());
            using var response = await _http.PostAsJsonAsync("v1/querybatch", request, Json, cancellationToken);
            response.EnsureSuccessStatusCode();
            var answer = await response.Content.ReadFromJsonAsync<BatchResponse>(Json, cancellationToken);
            var results = answer?.Results ?? new List<BatchResult>();
            for (var i = 0; i < batch.Length; i++)
            {
                idsPerPackage.Add(i < results.Count
                    ? (results[i].Vulns ?? new List<VulnRef>()).Select(v => v.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList()
                    : new List<string>());
            }
        }

        // An advisory can name several packages; its fixed versions are read for each package that has it
        var lookups = packages
            .SelectMany((p, i) => idsPerPackage[i].Select(id => (Id: id, Name: p.Name)))
            .Distinct()
            .ToList();
        using var gate = new SemaphoreSlim(MaxParallelDetails);
        var details = await Task.WhenAll(lookups.Select(async lookup =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (lookup.Id, lookup.Name, Advisory: await ReadAdvisoryAsync(lookup.Id, lookup.Name, ecosystem, cancellationToken) ?? Minimal(lookup.Id));
            }
            finally
            {
                gate.Release();
            }
        }));
        var byKey = details.ToDictionary(d => (d.Id, d.Name), d => d.Advisory);

        return packages
            .Select((p, i) => new OsvPackageResult(
                p.Name,
                p.Version,
                idsPerPackage[i]
                    .Select(id => byKey[(id, p.Name)])
                    .OrderBy(a => SeverityRank(a.Severity))
                    .ThenBy(a => a.Id, StringComparer.Ordinal)
                    .ToList()))
            .ToList();
    }

    private async Task<SecurityAdvisory?> ReadAdvisoryAsync(string id, string packageId, string ecosystem, CancellationToken cancellationToken)
    {
        try
        {
            var vuln = await _http.GetFromJsonAsync<Vulnerability>($"v1/vulns/{Uri.EscapeDataString(id)}", Json, cancellationToken);
            if (vuln is null)
                return Minimal(id);

            var malicious = id.StartsWith("MAL-", StringComparison.Ordinal);
            var fixedIn = (vuln.Affected ?? new List<Affected>())
                .Where(a => a.Package is not null
                            && string.Equals(a.Package.Ecosystem, ecosystem, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.Package.Name, packageId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(a => a.Ranges ?? new List<AffectedRange>())
                .SelectMany(r => r.Events ?? new List<RangeEvent>())
                .Select(e => e.Fixed)
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return new SecurityAdvisory
            {
                Id = vuln.Id ?? id,
                Aliases = vuln.Aliases ?? new List<string>(),
                Summary = SummaryOf(vuln),
                Severity = malicious ? AdvisorySeverity.Critical : SeverityOf(vuln),
                FixedIn = fixedIn,
                Url = $"https://osv.dev/vulnerability/{Uri.EscapeDataString(id)}",
                IsMalicious = malicious,
                Published = vuln.Published
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // The advisory still counts for its versions; only its details are missing
            _logger.LogWarning(ex, "OSV details for {AdvisoryId} could not be read", id);
            return Minimal(id);
        }
    }

    private static SecurityAdvisory Minimal(string id) => new()
    {
        Id = id,
        Severity = id.StartsWith("MAL-", StringComparison.Ordinal) ? AdvisorySeverity.Critical : AdvisorySeverity.Unknown,
        IsMalicious = id.StartsWith("MAL-", StringComparison.Ordinal),
        Url = $"https://osv.dev/vulnerability/{Uri.EscapeDataString(id)}"
    };

    private static string SummaryOf(Vulnerability vuln)
    {
        var text = !string.IsNullOrWhiteSpace(vuln.Summary)
            ? vuln.Summary!
            : (vuln.Details ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        text = text.Trim();
        return text.Length <= MaxSummaryLength ? text : text[..MaxSummaryLength] + "…";
    }

    // Only GitHub advisories carry a severity word; a bare CVSS vector is not scored, so stays unknown
    private static string SeverityOf(Vulnerability vuln)
    {
        var word = vuln.DatabaseSpecific is { ValueKind: JsonValueKind.Object } specific
                   && specific.TryGetProperty("severity", out var severity)
                   && severity.ValueKind == JsonValueKind.String
            ? severity.GetString()
            : null;

        return word?.ToUpperInvariant() switch
        {
            "CRITICAL" => AdvisorySeverity.Critical,
            "HIGH" => AdvisorySeverity.High,
            "MODERATE" or "MEDIUM" => AdvisorySeverity.Medium,
            "LOW" => AdvisorySeverity.Low,
            _ => AdvisorySeverity.Unknown
        };
    }

    private static int SeverityRank(string severity) => severity switch
    {
        AdvisorySeverity.Critical => 0,
        AdvisorySeverity.High => 1,
        AdvisorySeverity.Medium => 2,
        AdvisorySeverity.Low => 3,
        _ => 4
    };

    private static string? EcosystemOf(string registry) => RegistryNames.Canonical(registry) switch
    {
        RegistryNames.NuGet => "NuGet",
        RegistryNames.Npm => "npm",
        RegistryNames.PyPI => "PyPI",
        RegistryNames.PubDev => "Pub",
        RegistryNames.Packagist => "Packagist",
        _ => null
    };

    private sealed record BatchRequest(List<Query> Queries);

    private sealed record Query(PackageRef Package, string Version);

    private sealed record PackageRef(string Name, string Ecosystem);

    private sealed class BatchResponse
    {
        public List<BatchResult>? Results { get; set; }
    }

    private sealed class BatchResult
    {
        public List<VulnRef>? Vulns { get; set; }
    }

    private sealed class VulnRef
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class Vulnerability
    {
        public string? Id { get; set; }
        public string? Summary { get; set; }
        public string? Details { get; set; }
        public List<string>? Aliases { get; set; }
        public DateTime? Published { get; set; }
        public List<Affected>? Affected { get; set; }

        [JsonPropertyName("database_specific")]
        public JsonElement? DatabaseSpecific { get; set; }
    }

    private sealed class Affected
    {
        public AffectedPackage? Package { get; set; }
        public List<AffectedRange>? Ranges { get; set; }
    }

    private sealed class AffectedPackage
    {
        public string? Name { get; set; }
        public string? Ecosystem { get; set; }
    }

    private sealed class AffectedRange
    {
        public List<RangeEvent>? Events { get; set; }
    }

    private sealed class RangeEvent
    {
        public string? Fixed { get; set; }
    }
}
