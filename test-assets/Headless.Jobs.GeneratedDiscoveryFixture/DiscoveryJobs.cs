// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.GeneratedDiscoveryFixture;

public sealed record DiscoveryRequest(string Value);

/// <summary>Records what the generated invokers did, so a test can see each job ran with its dependencies.</summary>
public sealed class DiscoveryProbe
{
    private readonly List<object> _executions = [];
    private int _disposals;

    public IReadOnlyList<object> Executions => _executions;

    public int Disposals => Volatile.Read(ref _disposals);

    public void RecordExecution(object value) => _executions.Add(value);

    public void RecordDisposal() => Interlocked.Increment(ref _disposals);
}

[Job(FunctionName, Cron = "%Jobs:Discovery:Cron%", Priority = JobPriority.High, MaxConcurrency = 2)]
[JobScheduleMiddleware<DiscoveryScheduleMiddleware>]
public sealed class DiscoveryJobs(DiscoveryProbe probe) : IJob<DiscoveryRequest>
{
    public const string FunctionName = "tests.discovery.generated";

    public ValueTask ExecuteAsync(JobContext<DiscoveryRequest> context, CancellationToken cancellationToken)
    {
        probe.RecordExecution(context.Request);
        return ValueTask.CompletedTask;
    }
}

[Job(FunctionName)]
public sealed class DiscoveryCloseDay(DiscoveryProbe probe) : IJob, IDisposable
{
    public const string FunctionName = "tests.discovery.close-day";

    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        probe.RecordExecution(this);
        return ValueTask.CompletedTask;
    }

    public void Dispose() => probe.RecordDisposal();
}

public sealed class DiscoveryScheduleMiddleware : IJobScheduleMiddleware
{
    private static int _invocationCount;

    public static int InvocationCount => Volatile.Read(ref _invocationCount);

    public Task InvokeAsync(
        JobScheduleContext context,
        JobScheduleNext next,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _invocationCount);
        return next(cancellationToken);
    }
}
