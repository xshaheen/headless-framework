// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class JobsAdmissionWorkItemTests : TestBase
{
    private static readonly JobExecutionState _Function = new() { FunctionName = "fn", JobId = Guid.NewGuid() };

    [Fact]
    public async Task claim_write_failure_is_swallowed_without_executing()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
            .Returns<Task<JobExecutionState[]>>(_ => throw new InvalidOperationException("store down"));
        var work = JobsAdmissionWorkItem.Create(manager, null!, NullLogger.Instance, _Function, isDue: false);

        await work(AbortToken);

        await manager.Received(1).SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task lost_claim_race_skips_execution()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Array.Empty<JobExecutionState>()));

        // A null handler proves execution is never reached: the delegate would throw on it.
        var work = JobsAdmissionWorkItem.Create(manager, null!, NullLogger.Instance, _Function, isDue: false);

        await work(AbortToken);

        await manager.Received(1).SetTickersInProgress(Arg.Any<JobExecutionState[]>(), Arg.Any<CancellationToken>());
    }
}
