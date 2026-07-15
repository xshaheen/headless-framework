// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

[assembly: JobScheduleMiddleware<Headless.Jobs.DiscoveryFixture.DiscoveryScheduleMiddleware>]

namespace Headless.Jobs.DiscoveryFixture;

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
