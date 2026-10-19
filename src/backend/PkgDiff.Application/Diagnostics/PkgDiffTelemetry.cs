using System.Diagnostics;
using System.Diagnostics.Metrics;
using PkgDiff.Shared.Constants;

namespace PkgDiff.Application.Diagnostics;

// The application's own metrics and traces, through the .NET APIs that OpenTelemetry collects:
// one Meter and one ActivitySource, both named "PkgDiff". The /metrics endpoint publishes the meter
// as it is; an OpenTelemetry SDK added to the host only has to subscribe to this name.
// Tags are kept to small fixed sets. A package id goes on a trace, never on a metric.
public static class PkgDiffTelemetry
{
    public const string Name = "PkgDiff";

    public static readonly ActivitySource Activities = new(Name);

    public static readonly Meter Meter = new(Name);

    private static readonly Counter<long> Analyses = Meter.CreateCounter<long>(
        "analyses", "{analysis}", "Package analyses, by registry and outcome");

    private static readonly Histogram<double> AnalysisDuration = Meter.CreateHistogram<double>(
        "analysis.duration", "s", "Time one package analysis took");

    private static readonly Counter<long> Scrapes = Meter.CreateCounter<long>(
        "release_notes.scrapes", "{scrape}", "Release-note lookups for one version, by registry, source and outcome");

    public static async Task<T> MeasureAnalysisAsync<T>(string registry, string packageId, Func<Task<T>> analysis)
    {
        var registryTag = RegistryTag(registry);
        using var activity = Activities.StartActivity("pkgdiff.analyze");
        activity?.SetTag("pkgdiff.registry", registryTag);
        activity?.SetTag("pkgdiff.package", packageId);

        var started = Stopwatch.GetTimestamp();
        var outcome = "ok";
        try
        {
            return await analysis();
        }
        catch (Exception ex)
        {
            outcome = OutcomeOf(ex);
            if (outcome == "failed")
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }
        finally
        {
            activity?.SetTag("pkgdiff.outcome", outcome);
            var tags = new TagList { { "registry", registryTag }, { "outcome", outcome } };
            Analyses.Add(1, tags);
            AnalysisDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }

    public static Activity? StartScrape(string registry, string packageId, string version)
    {
        var activity = Activities.StartActivity("pkgdiff.scrape");
        activity?.SetTag("pkgdiff.registry", RegistryTag(registry));
        activity?.SetTag("pkgdiff.package", packageId);
        activity?.SetTag("pkgdiff.version", version);
        return activity;
    }

    public static void RecordScrape(Activity? activity, string registry, string source, bool found)
    {
        var outcome = found ? "found" : "empty";
        activity?.SetTag("pkgdiff.source", source);
        activity?.SetTag("pkgdiff.outcome", outcome);
        Scrapes.Add(1, new TagList { { "registry", RegistryTag(registry) }, { "source", source }, { "outcome", outcome } });
    }

    // A caller fault and an abandoned request are not failures of the application
    private static string OutcomeOf(Exception exception) => exception switch
    {
        OperationCanceledException => "cancelled",
        ArgumentException => "rejected",
        _ => "failed"
    };

    private static string RegistryTag(string registry) => RegistryNames.Canonical(registry) ?? "unknown";
}
