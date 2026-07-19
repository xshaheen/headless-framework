// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.GeneratedDiscoveryFixture;

public sealed record DiscoveryRequest;

public sealed class DiscoveryJobs
{
    public const string FunctionName = "tests.discovery.generated";

    [JobFunction(FunctionName, "%Jobs:Discovery:Cron%", JobPriority.High, 2)]
    [JobScheduleMiddleware<DiscoveryScheduleMiddleware>]
    public static Task RunAsync(JobFunctionContext<DiscoveryRequest> context, CancellationToken cancellationToken) =>
        Task.CompletedTask;
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
