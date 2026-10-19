using System.Diagnostics;
using System.Diagnostics.Metrics;
using PkgDiff.Application.Diagnostics;

namespace PkgDiff.Tests.Diagnostics;

public class PkgDiffTelemetryTests
{
    // Other tests record on the same meter while this one runs; only what this test's own flow records is kept
    private static readonly AsyncLocal<List<(string Instrument, double Value, Dictionary<string, object?> Tags)>?> Recorded = new();

    private static MeterListener Listen()
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == PkgDiffTelemetry.Name)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Keep(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Keep(instrument, value, tags));
        listener.Start();
        return listener;
    }

    private static void Keep(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        Recorded.Value?.Add((instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));

    [Fact]
    public async Task An_analysis_is_counted_and_timed_by_registry_and_outcome()
    {
        using var listener = Listen();
        Recorded.Value = [];

        var result = await PkgDiffTelemetry.MeasureAnalysisAsync("NPM", "left-pad", () => Task.FromResult(42));

        Assert.Equal(42, result);
        var count = Assert.Single(Recorded.Value, m => m.Instrument == "analyses");
        var duration = Assert.Single(Recorded.Value, m => m.Instrument == "analysis.duration");
        Assert.Equal(1, count.Value);
        Assert.Equal(("npm", "ok"), (count.Tags["registry"], count.Tags["outcome"]));
        Assert.Equal(("npm", "ok"), (duration.Tags["registry"], duration.Tags["outcome"]));
        Assert.InRange(duration.Value, 0, 5);

        // A package id would give the metric one series per package
        Assert.Equal(new[] { "outcome", "registry" }, count.Tags.Keys.Order());
    }

    [Theory]
    [InlineData(typeof(ArgumentException), "rejected")]
    [InlineData(typeof(OperationCanceledException), "cancelled")]
    [InlineData(typeof(TaskCanceledException), "cancelled")]
    [InlineData(typeof(InvalidOperationException), "failed")]
    public async Task The_outcome_tells_a_caller_fault_and_an_abandoned_request_from_a_failure(Type exception, string outcome)
    {
        using var listener = Listen();
        Recorded.Value = [];

        await Assert.ThrowsAsync(exception, () => PkgDiffTelemetry.MeasureAnalysisAsync<int>(
            "made-up-registry", "left-pad", () => throw (Exception)Activator.CreateInstance(exception)!));

        var count = Assert.Single(Recorded.Value, m => m.Instrument == "analyses");
        Assert.Equal(("unknown", outcome), (count.Tags["registry"], count.Tags["outcome"]));
    }

    [Fact]
    public async Task Analyses_and_scrapes_are_spans_of_the_PkgDiff_source()
    {
        var package = $"traced-{Guid.NewGuid():N}";
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PkgDiffTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (spans)
                    spans.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);

        await PkgDiffTelemetry.MeasureAnalysisAsync("pub", package, () =>
        {
            using var scrape = PkgDiffTelemetry.StartScrape("pub", package, "1.2.0");
            PkgDiffTelemetry.RecordScrape(scrape, "pub", "PubDev", found: true);
            return Task.FromResult(0);
        });

        List<Activity> mine;
        lock (spans)
            mine = spans.Where(span => (string?)span.GetTagItem("pkgdiff.package") == package).ToList();
        var analysis = Assert.Single(mine, span => span.OperationName == "pkgdiff.analyze");
        var scrapeSpan = Assert.Single(mine, span => span.OperationName == "pkgdiff.scrape");
        Assert.Equal(("pub.dev", "ok"), (analysis.GetTagItem("pkgdiff.registry"), analysis.GetTagItem("pkgdiff.outcome")));
        Assert.Equal(("1.2.0", "PubDev", "found"),
            (scrapeSpan.GetTagItem("pkgdiff.version"), scrapeSpan.GetTagItem("pkgdiff.source"), scrapeSpan.GetTagItem("pkgdiff.outcome")));
        Assert.Equal(analysis.SpanId, scrapeSpan.ParentSpanId);
    }
}
