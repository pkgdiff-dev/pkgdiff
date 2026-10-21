using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.RepresentationModel;

namespace PkgDiff.Infrastructure.Services;

// Reads packages from a pasted dependency file; the type is detected from the content.
// Each requirement becomes one version: the pinned one, else the lowest it allows.
public partial class ManifestParser : IManifestParser
{
    private const string Dev = "dev";
    private const string Optional = "optional";

    public ManifestParseResult Parse(string registry, string content, string? fileName)
    {
        var canonical = RegistryNames.Canonical(registry) ?? throw new ArgumentException("Unknown registry.");
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("The file is empty.");

        content = content.Trim().TrimStart('﻿');
        var result = canonical switch
        {
            RegistryNames.Npm => ParseNpm(content),
            RegistryNames.NuGet => ParseNuGet(content),
            RegistryNames.PyPI => ParsePyPI(content, fileName),
            RegistryNames.PubDev => ParsePub(content),
            RegistryNames.Packagist => ParseComposer(content),
            _ => throw new ArgumentException("Unknown registry.")
        };
        result.Registry = canonical;
        return Finish(result);
    }

    // One entry per package (the first one wins), capped at the audit limit
    private static ManifestParseResult Finish(ManifestParseResult result)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<AuditDependency>();
        foreach (var dependency in result.Dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency.Name) || !seen.Add(dependency.Name.Trim()))
                continue;
            dependency.Name = dependency.Name.Trim();
            unique.Add(dependency);
        }

        if (unique.Count == 0)
            throw new ArgumentException($"No packages were found in this {result.Format}.");

        if (unique.Count > AuditLimits.MaxDependencies)
        {
            result.Warnings.Add($"The file names {unique.Count} packages; the first {AuditLimits.MaxDependencies} are checked.");
            unique = unique.Take(AuditLimits.MaxDependencies).ToList();
        }

        result.Dependencies = unique;
        return result;
    }

    private static ManifestParseResult ParseNpm(string content)
    {
        var root = ParseJson(content, "a package.json or package-lock.json");

        if (root.TryGetProperty("lockfileVersion", out _))
            return ParsePackageLock(root);

        if (root.TryGetProperty("dependencies", out _) || root.TryGetProperty("devDependencies", out _))
            return ParsePackageJson(root);

        throw new ArgumentException("This JSON has neither dependencies nor a lockfileVersion: paste a package.json or a package-lock.json.");
    }

    private static ManifestParseResult ParsePackageJson(JsonElement root)
    {
        var result = new ManifestParseResult { Format = "package.json" };
        foreach (var (section, scope) in new[] { ("dependencies", (string?)null), ("devDependencies", Dev), ("optionalDependencies", Optional) })
        {
            if (!root.TryGetProperty(section, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var dependency in dependencies.EnumerateObject())
            {
                var requirement = dependency.Value.ValueKind == JsonValueKind.String ? dependency.Value.GetString() : null;
                result.Dependencies.Add(FromNpmRange(dependency.Name, requirement, scope, result.Warnings));
            }
        }
        return result;
    }

    private static ManifestParseResult ParsePackageLock(JsonElement root)
    {
        var result = new ManifestParseResult { Format = "package-lock.json" };

        // lockfileVersion 2 and 3: "packages" keyed by install path
        if (root.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Object)
        {
            foreach (var package in packages.EnumerateObject())
            {
                var index = package.Name.LastIndexOf("node_modules/", StringComparison.Ordinal);
                if (index < 0 || package.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (package.Value.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.True)
                    continue;
                var version = Text(package.Value, "version");
                if (version is null)
                    continue;
                var dev = package.Value.TryGetProperty("dev", out var devFlag) && devFlag.ValueKind == JsonValueKind.True;
                result.Dependencies.Add(Exact(package.Name[(index + "node_modules/".Length)..], version, dev ? Dev : null));
            }
            return result;
        }

        // lockfileVersion 1: nested "dependencies"
        if (root.TryGetProperty("dependencies", out var dependencies))
            ReadLockV1(dependencies, result);
        return result;
    }

    private static void ReadLockV1(JsonElement dependencies, ManifestParseResult result)
    {
        if (dependencies.ValueKind != JsonValueKind.Object)
            return;
        foreach (var dependency in dependencies.EnumerateObject())
        {
            var version = Text(dependency.Value, "version");
            if (version is not null && SemVerStart().IsMatch(version))
            {
                var dev = dependency.Value.TryGetProperty("dev", out var devFlag) && devFlag.ValueKind == JsonValueKind.True;
                result.Dependencies.Add(Exact(dependency.Name, version, dev ? Dev : null));
            }
            if (dependency.Value.TryGetProperty("dependencies", out var nested))
                ReadLockV1(nested, result);
        }
    }

    private static AuditDependency FromNpmRange(string name, string? requirement, string? scope, List<string> warnings)
    {
        var range = requirement?.Trim() ?? string.Empty;
        if (range.Length == 0 || range is "*" or "latest" or "x" || range.Contains(':') || range.Contains('/'))
        {
            if (range.Contains(':') || range.Contains('/'))
                warnings.Add($"{name}: \"{range}\" is not a registry version; its latest version is checked.");
            return new AuditDependency { Name = name, Requirement = requirement, Scope = scope };
        }

        // "^1.2.0 || ^2.0.0" allows several ranges; the first one is checked
        var first = range.Split("||")[0].Trim();
        var exact = SemVerExact().IsMatch(first);
        var match = SemVerStart().Match(first);
        return new AuditDependency
        {
            Name = name,
            Requirement = requirement,
            Scope = scope,
            IsExact = exact,
            Version = match.Success ? Complete(match.Value) : null
        };
    }

    private static ManifestParseResult ParseNuGet(string content)
    {
        if (content.StartsWith('{'))
            return ParseNuGetLock(ParseJson(content, "a packages.lock.json"));

        XDocument document;
        try
        {
            // No DTD: a dependency file needs none, and one could expand entities (billion laughs)
            using var reader = XmlReader.Create(new StringReader(content), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            throw new ArgumentException("This is not valid XML: paste a .csproj, Directory.Packages.props, packages.config or packages.lock.json.");
        }

        var elements = document.Descendants().ToList();
        var references = elements.Where(e => e.Name.LocalName == "PackageReference").ToList();
        var centralVersions = elements.Where(e => e.Name.LocalName == "PackageVersion").ToList();
        var configPackages = document.Root?.Name.LocalName == "packages"
            ? elements.Where(e => e.Name.LocalName == "package").ToList()
            : new List<XElement>();

        var result = new ManifestParseResult();
        if (configPackages.Count > 0)
        {
            result.Format = "packages.config";
            foreach (var package in configPackages)
            {
                var id = Attribute(package, "id");
                var version = Attribute(package, "version");
                if (id is null)
                    continue;
                var dev = string.Equals(Attribute(package, "developmentDependency"), "true", StringComparison.OrdinalIgnoreCase);
                result.Dependencies.Add(FromNuGetRange(id, version, dev ? Dev : null, result.Warnings));
            }
            return result;
        }

        if (centralVersions.Count > 0 && references.Count == 0)
        {
            result.Format = "Directory.Packages.props";
            foreach (var package in centralVersions)
            {
                var id = Attribute(package, "Include");
                if (id is not null)
                    result.Dependencies.Add(FromNuGetRange(id, Attribute(package, "Version") ?? Child(package, "Version"), null, result.Warnings));
            }
            return result;
        }

        if (references.Count > 0)
        {
            result.Format = ".csproj";
            var central = centralVersions
                .Select(e => (Id: Attribute(e, "Include"), Version: Attribute(e, "Version") ?? Child(e, "Version")))
                .Where(e => e.Id is not null)
                .GroupBy(e => e.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Version, StringComparer.OrdinalIgnoreCase);
            foreach (var reference in references)
            {
                var id = Attribute(reference, "Include") ?? Attribute(reference, "Update");
                if (id is null)
                    continue;
                var version = Attribute(reference, "Version") ?? Child(reference, "Version")
                              ?? Attribute(reference, "VersionOverride") ?? (central.TryGetValue(id, out var v) ? v : null);
                if (version is null)
                    result.Warnings.Add($"{id}: no version in this file (central package management?); its latest version is checked.");
                result.Dependencies.Add(FromNuGetRange(id, version, null, result.Warnings));
            }
            return result;
        }

        throw new ArgumentException("No PackageReference, PackageVersion or package elements were found in this XML.");
    }

    private static ManifestParseResult ParseNuGetLock(JsonElement root)
    {
        var result = new ManifestParseResult { Format = "packages.lock.json" };
        if (!root.TryGetProperty("dependencies", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("This JSON has no \"dependencies\": paste a packages.lock.json.");

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var package in framework.Value.EnumerateObject())
            {
                var type = Text(package.Value, "type");
                if (type == "Project")
                    continue;
                var resolved = Text(package.Value, "resolved");
                if (resolved is not null)
                    result.Dependencies.Add(Exact(package.Name, resolved, null));
            }
        }
        return result;
    }

    private static AuditDependency FromNuGetRange(string id, string? requirement, string? scope, List<string> warnings)
    {
        var value = requirement?.Trim();
        if (string.IsNullOrEmpty(value))
            return new AuditDependency { Name = id, Scope = scope };

        if (value.Contains("$(") || value.Contains('*'))
        {
            warnings.Add($"{id}: \"{value}\" is not a fixed version; its latest version is checked.");
            return new AuditDependency { Name = id, Requirement = value, Scope = scope };
        }

        // "[1.2.3]" pins, "[1.0,2.0)" and "1.0" set a lower bound
        var exact = value.StartsWith('[') && value.EndsWith(']') && !value.Contains(',');
        var lower = value.TrimStart('[', '(').Split(',')[0].TrimEnd(']', ')').Trim();
        return new AuditDependency
        {
            Name = id,
            Requirement = value,
            Scope = scope,
            IsExact = exact,
            Version = lower.Length > 0 ? lower : null
        };
    }

    private static ManifestParseResult ParsePyPI(string content, string? fileName)
    {
        if (content.StartsWith('{'))
            return ParsePipfileLock(ParseJson(content, "a Pipfile.lock"));

        var looksLikeToml = TomlSection().IsMatch(content)
                            || (fileName ?? string.Empty).EndsWith(".toml", StringComparison.OrdinalIgnoreCase)
                            || (fileName ?? string.Empty).EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(fileName, "Pipfile", StringComparison.OrdinalIgnoreCase);
        return looksLikeToml ? ParsePythonToml(content) : ParseRequirements(content);
    }

    private static ManifestParseResult ParseRequirements(string content)
    {
        var result = new ManifestParseResult { Format = "requirements.txt" };
        foreach (var rawLine in content.Split('\n'))
        {
            // A trailing backslash continues the line (hashes follow on the next one)
            var line = rawLine.Trim().TrimEnd('\\').Trim();
            var comment = line.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0)
                line = line[..comment].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('-'))
                continue;
            if (line.Contains("://") || line.StartsWith('.') || line.StartsWith('/'))
            {
                result.Warnings.Add($"\"{Shorten(line)}\" is not a package from PyPI; it is skipped.");
                continue;
            }
            var dependency = FromPep508(line, null);
            if (dependency is not null)
                result.Dependencies.Add(dependency);
        }
        return result;
    }

    private static ManifestParseResult ParsePythonToml(string content)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(content)!;
        }
        catch (Exception ex) when (ex is TomlException or InvalidOperationException or ArgumentException)
        {
            throw new ArgumentException("This TOML could not be read: paste a pyproject.toml, poetry.lock or Pipfile.");
        }

        // poetry.lock: [[package]] name, version
        if (table.TryGetValue("package", out var lockedPackages) && lockedPackages is TomlTableArray packages)
        {
            var result = new ManifestParseResult { Format = "poetry.lock" };
            foreach (var package in packages)
            {
                var name = TomlText(package, "name");
                var version = TomlText(package, "version");
                if (name is not null && version is not null)
                    result.Dependencies.Add(Exact(name, version, TomlText(package, "category") == "dev" ? Dev : null));
            }
            return result;
        }

        // Pipfile: [packages] and [dev-packages]
        if (table.ContainsKey("packages") || table.ContainsKey("dev-packages"))
        {
            var result = new ManifestParseResult { Format = "Pipfile" };
            ReadPoetryStyle(table, "packages", null, result);
            ReadPoetryStyle(table, "dev-packages", Dev, result);
            return result;
        }

        var pyproject = new ManifestParseResult { Format = "pyproject.toml" };
        if (table.TryGetValue("project", out var projectValue) && projectValue is TomlTable project)
        {
            if (project.TryGetValue("dependencies", out var dependencies) && dependencies is TomlArray list)
                AddPep508(list, null, pyproject);
            if (project.TryGetValue("optional-dependencies", out var optional) && optional is TomlTable groups)
            {
                foreach (var group in groups)
                {
                    if (group.Value is TomlArray groupList)
                        AddPep508(groupList, Optional, pyproject);
                }
            }
        }

        if (table.TryGetValue("tool", out var toolValue) && toolValue is TomlTable tool
            && tool.TryGetValue("poetry", out var poetryValue) && poetryValue is TomlTable poetry)
        {
            ReadPoetryStyle(poetry, "dependencies", null, pyproject);
            ReadPoetryStyle(poetry, "dev-dependencies", Dev, pyproject);
            if (poetry.TryGetValue("group", out var groupsValue) && groupsValue is TomlTable poetryGroups)
            {
                foreach (var group in poetryGroups)
                {
                    if (group.Value is TomlTable groupTable)
                        ReadPoetryStyle(groupTable, "dependencies", Dev, pyproject);
                }
            }
        }

        if (pyproject.Dependencies.Count == 0 && !table.ContainsKey("project") && !table.ContainsKey("tool"))
            throw new ArgumentException("No dependencies were found: paste a pyproject.toml, poetry.lock or Pipfile.");
        return pyproject;
    }

    private static void AddPep508(TomlArray list, string? scope, ManifestParseResult result)
    {
        foreach (var item in list)
        {
            if (item is string requirement && FromPep508(requirement, scope) is { } dependency)
                result.Dependencies.Add(dependency);
        }
    }

    private static void ReadPoetryStyle(TomlTable parent, string key, string? scope, ManifestParseResult result)
    {
        if (!parent.TryGetValue(key, out var value) || value is not TomlTable dependencies)
            return;
        foreach (var dependency in dependencies)
        {
            if (string.Equals(dependency.Key, "python", StringComparison.OrdinalIgnoreCase))
                continue;
            var requirement = dependency.Value switch
            {
                string text => text,
                TomlTable details => TomlText(details, "version"),
                _ => null
            };
            if (dependency.Value is TomlTable sourceTable && (sourceTable.ContainsKey("git") || sourceTable.ContainsKey("path") || sourceTable.ContainsKey("url")))
            {
                result.Warnings.Add($"{dependency.Key}: not a package from PyPI; it is skipped.");
                continue;
            }
            result.Dependencies.Add(FromPythonConstraint(dependency.Key, requirement, scope));
        }
    }

    private static ManifestParseResult ParsePipfileLock(JsonElement root)
    {
        var result = new ManifestParseResult { Format = "Pipfile.lock" };
        foreach (var (section, scope) in new[] { ("default", (string?)null), ("develop", Dev) })
        {
            if (!root.TryGetProperty(section, out var packages) || packages.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var package in packages.EnumerateObject())
            {
                var version = Text(package.Value, "version");
                if (version is not null)
                    result.Dependencies.Add(Exact(package.Name, version.TrimStart('='), scope));
            }
        }
        if (result.Dependencies.Count == 0 && !root.TryGetProperty("_meta", out _))
            throw new ArgumentException("This JSON is not a Pipfile.lock.");
        return result;
    }

    private static AuditDependency? FromPep508(string line, string? scope)
    {
        var withoutMarker = line.Split(';')[0].Trim();
        var match = Pep508().Match(withoutMarker);
        if (!match.Success)
            return null;
        var name = match.Groups["name"].Value;
        var specifier = match.Groups["spec"].Value.Trim().Trim('(', ')').Trim();
        if (specifier.StartsWith('@'))
            return null;
        return FromPythonConstraint(name, specifier.Length == 0 ? null : specifier, scope);
    }

    private static AuditDependency FromPythonConstraint(string name, string? requirement, string? scope)
    {
        var value = requirement?.Trim();
        if (string.IsNullOrEmpty(value) || value == "*")
            return new AuditDependency { Name = name, Requirement = requirement, Scope = scope };

        var clauses = value.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
        var pinned = clauses.FirstOrDefault(c => c.StartsWith("===") || (c.StartsWith("==") && !c.Contains('*')));
        if (pinned is not null)
            return new AuditDependency { Name = name, Requirement = value, Scope = scope, IsExact = true, Version = pinned.TrimStart('=').Trim() };

        // The lowest version allowed: >=, ~=, ^ (Poetry), ~ (Poetry) or a bare version
        var lower = clauses
            .Select(c => PythonLowerBound().Match(c))
            .FirstOrDefault(m => m.Success);
        return new AuditDependency
        {
            Name = name,
            Requirement = value,
            Scope = scope,
            Version = lower?.Groups["version"].Value
        };
    }

    private static ManifestParseResult ParsePub(string content)
    {
        YamlMappingNode root;
        try
        {
            RejectDeepYaml(content);
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode
                   ?? throw new ArgumentException("This YAML is empty.");
        }
        catch (YamlDotNet.Core.YamlException)
        {
            throw new ArgumentException("This YAML could not be read: paste a pubspec.yaml or pubspec.lock.");
        }

        // pubspec.lock: packages -> name -> { dependency, source, version }
        if (Map(root, "packages") is { } locked && Map(root, "dependencies") is null)
        {
            var result = new ManifestParseResult { Format = "pubspec.lock" };
            foreach (var (key, value) in locked.Children)
            {
                if (value is not YamlMappingNode package || Scalar(package, "source") != "hosted")
                    continue;
                var version = Scalar(package, "version");
                if (version is not null)
                    result.Dependencies.Add(Exact(((YamlScalarNode)key).Value!, version, Scalar(package, "dependency") == "direct dev" ? Dev : null));
            }
            return result;
        }

        if (Map(root, "dependencies") is null && Map(root, "dev_dependencies") is null)
            throw new ArgumentException("No dependencies were found: paste a pubspec.yaml or pubspec.lock.");

        var pubspec = new ManifestParseResult { Format = "pubspec.yaml" };
        foreach (var (section, scope) in new[] { ("dependencies", (string?)null), ("dev_dependencies", Dev) })
        {
            if (Map(root, section) is not { } dependencies)
                continue;
            foreach (var (key, value) in dependencies.Children)
            {
                var name = ((YamlScalarNode)key).Value!;
                string? requirement = null;
                if (value is YamlScalarNode scalar)
                {
                    requirement = scalar.Value;
                }
                else if (value is YamlMappingNode details)
                {
                    if (Scalar(details, "sdk") is not null)
                        continue;
                    if (details.Children.ContainsKey(new YamlScalarNode("git")) || details.Children.ContainsKey(new YamlScalarNode("path")))
                    {
                        pubspec.Warnings.Add($"{name}: not a package from pub.dev; it is skipped.");
                        continue;
                    }
                    requirement = Scalar(details, "version");
                }
                pubspec.Dependencies.Add(FromPubConstraint(name, requirement, scope));
            }
        }
        return pubspec;
    }

    // YamlStream recurses and a stack overflow kills the process, so depth is measured first
    private const int MaxYamlDepth = 64;

    private static void RejectDeepYaml(string content)
    {
        var parser = new YamlDotNet.Core.Parser(new StringReader(content));
        var depth = 0;
        while (parser.MoveNext())
        {
            switch (parser.Current)
            {
                case YamlDotNet.Core.Events.MappingStart or YamlDotNet.Core.Events.SequenceStart:
                    if (++depth > MaxYamlDepth)
                        throw new ArgumentException("This YAML is nested too deeply to be a pubspec.yaml or pubspec.lock.");
                    break;
                case YamlDotNet.Core.Events.MappingEnd or YamlDotNet.Core.Events.SequenceEnd:
                    depth--;
                    break;
            }
        }
    }

    private static AuditDependency FromPubConstraint(string name, string? requirement, string? scope)
    {
        var value = requirement?.Trim();
        if (string.IsNullOrEmpty(value) || value == "any")
            return new AuditDependency { Name = name, Requirement = requirement, Scope = scope };
        var match = SemVerStart().Match(value);
        return new AuditDependency
        {
            Name = name,
            Requirement = value,
            Scope = scope,
            IsExact = SemVerExact().IsMatch(value),
            Version = match.Success ? Complete(match.Value) : null
        };
    }

    private static ManifestParseResult ParseComposer(string content)
    {
        var root = ParseJson(content, "a composer.json or composer.lock");

        if (root.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Array)
        {
            var lockResult = new ManifestParseResult { Format = "composer.lock" };
            ReadComposerLock(packages, null, lockResult);
            if (root.TryGetProperty("packages-dev", out var devPackages) && devPackages.ValueKind == JsonValueKind.Array)
                ReadComposerLock(devPackages, Dev, lockResult);
            return lockResult;
        }

        if (!root.TryGetProperty("require", out _) && !root.TryGetProperty("require-dev", out _))
            throw new ArgumentException("This JSON has neither require nor packages: paste a composer.json or a composer.lock.");

        var result = new ManifestParseResult { Format = "composer.json" };
        foreach (var (section, scope) in new[] { ("require", (string?)null), ("require-dev", Dev) })
        {
            if (!root.TryGetProperty(section, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var dependency in dependencies.EnumerateObject())
            {
                // php, ext-*, lib-* and composer-plugin-api name the platform, not a package
                if (!dependency.Name.Contains('/'))
                    continue;
                var requirement = dependency.Value.ValueKind == JsonValueKind.String ? dependency.Value.GetString() : null;
                result.Dependencies.Add(FromComposerConstraint(dependency.Name.ToLowerInvariant(), requirement, scope, result.Warnings));
            }
        }
        return result;
    }

    private static void ReadComposerLock(JsonElement packages, string? scope, ManifestParseResult result)
    {
        foreach (var package in packages.EnumerateArray())
        {
            var name = Text(package, "name");
            var version = Text(package, "version");
            if (name is null || !name.Contains('/'))
                continue;
            if (version is null || version.StartsWith("dev-", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add($"{name}: \"{version}\" is a branch, not a release; its latest version is checked.");
                result.Dependencies.Add(new AuditDependency { Name = name.ToLowerInvariant(), Requirement = version, Scope = scope });
                continue;
            }
            result.Dependencies.Add(Exact(name.ToLowerInvariant(), StripComposerV(version), scope));
        }
    }

    private static AuditDependency FromComposerConstraint(string name, string? requirement, string? scope, List<string> warnings)
    {
        var value = requirement?.Trim() ?? string.Empty;
        if (value.Length == 0 || value == "*")
            return new AuditDependency { Name = name, Requirement = requirement, Scope = scope };

        if (value.Contains("dev-", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{name}: \"{value}\" is a branch, not a release; its latest version is checked.");
            return new AuditDependency { Name = name, Requirement = requirement, Scope = scope };
        }

        // "^1.0 || ^2.0" allows several ranges; the first one is checked, without its @stability flag
        var first = (value.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value).Trim().Split('@')[0];
        var exact = SemVerExact().IsMatch(first);
        var match = SemVerStart().Match(first);
        return new AuditDependency
        {
            Name = name,
            Requirement = requirement,
            Scope = scope,
            IsExact = exact,
            Version = match.Success ? Complete(match.Value) : null
        };
    }

    private static string StripComposerV(string version) =>
        version.Length > 1 && (version[0] == 'v' || version[0] == 'V') && char.IsDigit(version[1]) ? version[1..] : version;

    private static JsonElement ParseJson(string content, string expected)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new ArgumentException($"This is not valid JSON: paste {expected}.");
        }
    }

    private static AuditDependency Exact(string name, string version, string? scope) =>
        new() { Name = name, Version = version.Trim(), Requirement = version.Trim(), IsExact = true, Scope = scope };

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string? Child(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private static string? TomlText(TomlTable table, string key) =>
        table.TryGetValue(key, out var value) ? value as string : null;

    private static YamlMappingNode? Map(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value as YamlMappingNode : null;

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? (value as YamlScalarNode)?.Value : null;

    // "1" and "1.2" become "1.0.0" and "1.2.0", the spelling the registries and OSV use
    private static string Complete(string version)
    {
        var core = version.Split('-', '+')[0];
        var rest = version[core.Length..];
        var parts = core.Split('.').ToList();
        while (parts.Count < 3)
            parts.Add("0");
        return string.Join('.', parts) + rest;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "…";

    [GeneratedRegex(@"\d+(?:\.\d+){0,2}(?:-[0-9A-Za-z.-]+)?")]
    private static partial Regex SemVerStart();

    [GeneratedRegex(@"^=?v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemVerExact();

    [GeneratedRegex(@"^\s*\[[^\]]+\]\s*$", RegexOptions.Multiline)]
    private static partial Regex TomlSection();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)\s*(?:\[[^\]]*\])?\s*(?<spec>.*)$")]
    private static partial Regex Pep508();

    [GeneratedRegex(@"^(?:>=|~=|\^|~|==|>)?\s*(?<version>\d+(?:\.\d+)*(?:[a-zA-Z0-9.+-]*)?)$")]
    private static partial Regex PythonLowerBound();
}
