using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

public class NpmService : INpmService
{
    private const VersionScheme Scheme = VersionScheme.SemVer;

    private readonly HttpClient _httpClient;
    private readonly ILogger<NpmService>? _logger;

    public NpmService(IHttpClientFactory httpClientFactory, ILogger<NpmService>? logger = null)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.NpmRegistry);
        _logger = logger;
    }

    public async Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false)
    {
        var packageData = await GetPackageDataAsync(packageId);
        if (packageData?.Versions == null)
        {
            return Enumerable.Empty<string>();
        }

        var versions = PackageVersions.ParseAll(Scheme, packageData.Versions.Keys, includePrerelease, out var skipped);
        if (skipped.Count > 0)
        {
            _logger?.LogWarning(
                "Skipped {Count} versions of {PackageId} that are not valid SemVer: {Versions}",
                skipped.Count, packageId, string.Join(", ", skipped.Take(10)));
        }

        return versions.Select(v => v.Value).ToList();
    }

    public async Task<IEnumerable<string>> GetVersionsInRangeAsync(
        string packageId, 
        string fromVersion, 
        string toVersion, 
        bool includePrerelease = false)
    {
        var allVersions = await GetPackageVersionsAsync(packageId, includePrerelease);
        var fromParsed = TryParseVersion(fromVersion);
        var toParsed = TryParseVersion(toVersion);

        if (fromParsed == null || toParsed == null)
        {
            return Enumerable.Empty<string>();
        }

        return allVersions
            .Select(v => new { Version = v, Parsed = TryParseVersion(v) })
            .Where(x => x.Parsed != null && x.Parsed > fromParsed && x.Parsed <= toParsed)
            .OrderBy(x => x.Parsed)
            .Select(x => x.Version)
            .ToList();
    }

    public async Task<bool> PackageExistsAsync(string packageId)
    {
        try
        {
            var packageData = await GetPackageDataAsync(packageId);
            return packageData != null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "npm lookup failed for {PackageId}", packageId);
            return false;
        }
    }

    public async Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version)
    {
        try
        {
            var packageData = await GetPackageDataAsync(packageId);
            if (packageData?.Time != null && packageData.Time.ContainsKey(version))
            {
                return packageData.Time[version];
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "npm lookup failed for {PackageId}", packageId);
            return null;
        }
    }

    public async Task<PackageMetadata> GetPackageMetadataAsync(string packageId)
    {
        var packageData = await GetPackageDataAsync(packageId);
        if (packageData == null)
        {
            throw new ArgumentException($"Package '{packageId}' not found on npm.");
        }

        return MapToPackageMetadata(packageId, packageData);
    }

    private async Task<NpmPackageData?> GetPackageDataAsync(string packageId)
    {
        try
        {
            // Streamed: large packuments exceed the client's buffer cap and only small fields are kept
            using var response = await _httpClient.GetAsync(
                PackageUrlEncoder.EscapeNpmPackagePath(packageId), HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync();
            await using var limited = new LengthLimitedStream(body, MaxPackumentBytes);
            return await JsonSerializer.DeserializeAsync<NpmPackageData>(limited);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "npm lookup failed for {PackageId}", packageId);
            return null;
        }
    }

    private const long MaxPackumentBytes = 256L * 1024 * 1024;

    private static bool IsPrerelease(string version) =>
        PackageVersion.TryParse(Scheme, version, out var parsed) && parsed.IsPrerelease;

    private static PackageVersion? TryParseVersion(string version) =>
        PackageVersion.TryParse(Scheme, version, out var parsed) ? parsed : null;

    private static bool IsGitHubUrl(string? url)
    {
        return GitHubUrls.IsGitHub(url);
    }

    private static string? CleanRepositoryUrl(string? repositoryUrl)
    {
        if (string.IsNullOrEmpty(repositoryUrl))
            return null;

        return GitHubUrls.FromRepositoryField(repositoryUrl);
    }

    private PackageMetadata MapToPackageMetadata(string packageId, NpmPackageData packageData)
    {
        var latestVersion = packageData.DistTags?.Latest ?? packageData.Versions?.Keys.LastOrDefault();
        var latestVersionData = latestVersion != null && packageData.Versions?.ContainsKey(latestVersion) == true
            ? packageData.Versions[latestVersion]
            : null;

        string? repositoryUrl = latestVersionData?.Repository?.Url;
        repositoryUrl = CleanRepositoryUrl(repositoryUrl);
        // A monorepo names the folder of this package, where its changelog usually is
        var directory = latestVersionData?.Repository?.Directory?.Trim('/', ' ');
        if (string.IsNullOrEmpty(directory) || directory.Contains(".."))
            directory = null;
        var homepage = latestVersionData?.Homepage;

        return new PackageMetadata
        {
            PackageId = packageId,
            LatestVersion = latestVersion ?? "unknown",
            Description = latestVersionData?.Description ?? packageData.Description ?? "",
            Authors = latestVersionData?.Author?.Name ?? packageData.Author?.Name ?? "Unknown",
            IconUrl = null,
            ProjectUrl = homepage ?? repositoryUrl,
            GitHubRepositoryUrl = IsGitHubUrl(repositoryUrl) ? repositoryUrl : null,
            SourceDirectory = IsGitHubUrl(repositoryUrl) ? directory : null,
            Versions = packageData.Versions?
                .Where(v => !IsPrerelease(v.Key))
                .Select(v => new VersionSummary
                {
                    Version = v.Key,
                    PublishedDate = packageData.Time?.ContainsKey(v.Key) == true ? packageData.Time[v.Key] : null,
                    IsPrerelease = false,
                })
                .OrderByDescending(v => TryParseVersion(v.Version))
                .ToList() ?? new List<VersionSummary>()
        };
    }

    private class NpmPackageData
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        [JsonConverter(typeof(LooseStringConverter))]
        public string? Description { get; set; }

        [JsonPropertyName("dist-tags")]
        public DistTags? DistTags { get; set; }

        [JsonPropertyName("versions")]
        public Dictionary<string, NpmVersion>? Versions { get; set; }

        [JsonPropertyName("time")]
        public Dictionary<string, DateTime>? Time { get; set; }

        [JsonPropertyName("author")]
        public NpmAuthor? Author { get; set; }
    }

    private class DistTags
    {
        [JsonPropertyName("latest")]
        public string? Latest { get; set; }
    }

    private class NpmVersion
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("description")]
        [JsonConverter(typeof(LooseStringConverter))]
        public string? Description { get; set; }

        [JsonPropertyName("author")]
        public NpmAuthor? Author { get; set; }

        [JsonPropertyName("repository")]
        public NpmRepository? Repository { get; set; }

        [JsonPropertyName("homepage")]
        [JsonConverter(typeof(LooseStringConverter))]
        public string? Homepage { get; set; }
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(NpmAuthorConverter))]
    private class NpmAuthor
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class NpmAuthorConverter : JsonConverter<NpmAuthor>
    {
        public override NpmAuthor? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                // "Name <email> (url)" string format
                var authorString = reader.GetString();
                var emailStart = authorString?.IndexOf('<') ?? -1;
                var name = emailStart > 0 ? authorString!.Substring(0, emailStart).Trim() : authorString;
                return new NpmAuthor { Name = name };
            }
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var author = new NpmAuthor();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var propName = reader.GetString();
                        reader.Read();
                        if (propName == "name") author.Name = reader.GetString();
                        else reader.Skip();
                    }
                }
                return author;
            }
            return null;
        }

        public override void Write(Utf8JsonWriter writer, NpmAuthor value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, new JsonSerializerOptions());
    }

    [System.Text.Json.Serialization.JsonConverter(typeof(NpmRepositoryConverter))]
    private class NpmRepository
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("directory")]
        public string? Directory { get; set; }
    }

    // "repository" is an object ({ "type", "url", "directory" }) or only its URL as a string
    private class NpmRepositoryConverter : JsonConverter<NpmRepository>
    {
        public override NpmRepository? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
                return new NpmRepository { Url = reader.GetString() };
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return null;
            }

            var repository = new NpmRepository();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;
                var propName = reader.GetString();
                reader.Read();
                var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    reader.Skip();
                switch (propName)
                {
                    case "type": repository.Type = value; break;
                    case "url": repository.Url = value; break;
                    case "directory": repository.Directory = value; break;
                }
            }
            return repository;
        }

        public override void Write(Utf8JsonWriter writer, NpmRepository value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, new JsonSerializerOptions());
    }

    // Lenient for odd old versions: first string of a list, text of a scalar, else null
    private class LooseStringConverter : JsonConverter<string?>
    {
        public override bool HandleNull => true;

        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                    return System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
                case JsonTokenType.StartArray:
                    string? first = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                            first ??= reader.GetString();
                        else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                            reader.Skip();
                    }
                    return first;
                case JsonTokenType.StartObject:
                    reader.Skip();
                    return null;
                default:
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    // Throws once more than the limit has been read, so a stream read is still bounded
    private sealed class LengthLimitedStream(Stream inner, long limit) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        private int Count(int read)
        {
            _read += read;
            if (_read > limit)
                throw new InvalidDataException($"The response is larger than {limit} bytes.");
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
