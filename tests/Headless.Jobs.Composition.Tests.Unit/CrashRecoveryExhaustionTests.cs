// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// Pins the executor half of the crash-durable retry budget: a row whose persisted RetryCount already exceeds
/// its budget (each InProgress reclaim consumed one unit) is terminalized Failed with the exhausted callback and
/// WITHOUT invoking the handler — the gate that stops a host-killing handler from running once per lease cycle
/// forever. A persisted count exactly AT the budget still runs its final allowed attempt.
/// </summary>
public sealed class CrashRecoveryExhaustionTests : TestBase
{
    [Fact]
    public async Task terminalizes_without_invoking_the_handler_when_recovery_exhausted_the_budget()
    {
        var manager = _HealthyManager();
        var exhausted = new List<JobExhaustedContext>();
        var handler = _Handler(
            manager,
            new JobsRetryOptions
            {
                OnExhausted = (context, _) =>
                {
                    exhausted.Add(context);
                    return Task.CompletedTask;
                },
            }
        );

        var invoked = false;
        var state = _Job(
            retries: 2,
            retryCount: 3,
            (_, _, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            }
        );

        await handler.ExecuteTaskAsync(state, isDue: false, cancellationToken: AbortToken);

        invoked.Should().BeFalse("the budget was already consumed by crash-recovery reclaims");
        await manager
            .Received(1)
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Failed), CancellationToken.None);
        exhausted.Should().ContainSingle().Which.RetryCount.Should().Be(3);
    }

    [Fact]
    public async Task runs_the_final_attempt_when_the_persisted_count_equals_the_budget()
    {
        var manager = _HealthyManager();
        var handler = _Handler(manager, retryOptions: null);

        var invoked = false;
        var state = _Job(
            retries: 2,
            retryCount: 2,
            (_, _, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            }
        );

        await handler.ExecuteTaskAsync(state, isDue: false, cancellationToken: AbortToken);

        invoked.Should().BeTrue("a count AT the budget is the final allowed attempt, not exhaustion");
        await manager
            .Received(1)
            .UpdateTickerAsync(Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Succeeded), CancellationToken.None);
    }

    [Fact]
    public async Task fenced_exhaustion_write_flags_lease_loss_and_skips_the_exhausted_callback()
    {
        var manager = _HealthyManager();
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
        var exhaustedInvoked = false;
        var handler = _Handler(
            manager,
            new JobsRetryOptions
            {
                OnExhausted = (_, _) =>
                {
                    exhaustedInvoked = true;
                    return Task.CompletedTask;
                },
            }
        );

        var state = _Job(retries: 1, retryCount: 5, (_, _, _) => Task.CompletedTask);

        await handler.ExecuteTaskAsync(state, isDue: false, cancellationToken: AbortToken);

        state.LeaseLost.Should().BeTrue("a 0-row terminal write means the row was reclaimed by another sweep");
        exhaustedInvoked.Should().BeFalse("callbacks fire only after the owned terminal write persists");
    }

    private static IInternalJobManager _HealthyManager()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        return manager;
    }

    private static JobsExecutionTaskHandler _Handler(IInternalJobManager manager, JobsRetryOptions? retryOptions)
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        var serviceProvider = services.BuildServiceProvider();
        return new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            retryOptions
        );
    }

    private static JobExecutionState _Job(int retries, int retryCount, JobFunctionDelegate function)
    {
        return new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "fn",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            Retries = retries,
            RetryCount = retryCount,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
            CachedDelegate = function,
        };
    }
}
