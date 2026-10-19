using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace PkgDiff.Tests.TestDoubles;

public sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> Jobs { get; } = new();

    public string Create(Job job, IState state)
    {
        Jobs.Add(job);
        return Jobs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
