// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class UnregisteredFunctionReleaseTests : TestBase
{
    [Fact]
    public async Task releases_the_row_when_the_function_is_not_registered_on_this_node()
    {
        // Rolling deploy: the enqueuing node runs a newer build, this node's registry lacks the function. The
        // claimed row previously reached a null delegate and retried the NullReferenceException through the whole
        // budget. It must be RELEASED (Idle, owner and lease cleared, RetryCount untouched) so a node that HAS
        // the registration can run it — a local registry gap must not poison the job.
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var state = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "not-on-this-node",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            RunCondition = RunCondition.OnSuccess,
            // CachedDelegate deliberately left unset — the registry miss shape.
        };

        await handler.ExecuteTaskAsync(state, isDue: false, cancellationToken: AbortToken);

        await manager
            .Received(1)
            .UpdateTickerAsync(
                Arg.Is<JobExecutionState>(x => x.Status == JobStatus.Idle && x.ReleaseLock),
                CancellationToken.None
            );
    }

    [Theory]
    [InlineData(RunCondition.InProgress)]
    [InlineData(RunCondition.OnSuccess)]
    public async Task releases_an_unregistered_chain_child_when_its_condition_matches(RunCondition runCondition)
    {
        var updates = new ConcurrentQueue<(Guid JobId, JobStatus Status, bool ReleaseLock, CancellationToken Token)>();
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var state = call.Arg<JobExecutionState>();
                updates.Enqueue((state.JobId, state.Status, state.ReleaseLock, call.Arg<CancellationToken>()));
                return Task.FromResult(1);
            });
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        var parent = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "registered-parent",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            CachedDelegate = (_, _, _) => Task.CompletedTask,
        };
        var child = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            ParentId = parent.JobId,
            FunctionName = "not-on-this-node",
            Type = JobType.TimeJob,
            ExecutionTime = DateTime.UtcNow,
            RetryIntervals = [0],
            Status = JobStatus.Idle,
            RunCondition = runCondition,
            // CachedDelegate deliberately left unset — the registry miss shape.
        };
        parent.TimeJobChildren.Add(child);

        await handler.ExecuteTaskAsync(parent, isDue: false, cancellationToken: AbortToken);

        updates
            .Should()
            .ContainSingle(update =>
                update.JobId == child.JobId
                && update.Status == JobStatus.Idle
                && update.ReleaseLock
                && update.Token == CancellationToken.None
            );
    }

    [Fact]
    public async Task worker_pool_logs_a_faulted_work_item_instead_of_swallowing_it_silently()
    {
        // The pool's catch-alls keep the worker alive by design, but a vanished failure between the admission's
        // claim check and the terminal-status write means a job re-runs after lease lapse with zero log lines
        // connecting the two runs. Pin that the swallow is at least observable.
        var logger = new CapturingPoolLogger();
        await using var scheduler = new JobsTaskScheduler(
            maxConcurrency: 1,
            timeProvider: TimeProvider.System,
            logger: logger
        );

        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await scheduler.QueueAsync(
            _ =>
            {
                faulted.TrySetResult();
                throw new InvalidOperationException("terminal write failed");
            },
            JobPriority.Normal,
            AbortToken
        );
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!logger.HasError)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the pool must log the swallowed work-item fault");
            await Task.Delay(10, AbortToken);
        }
    }

    private sealed class CapturingPoolLogger : ILogger<JobsTaskScheduler>
    {
        private volatile bool _hasError;

        public bool HasError => _hasError;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Error && exception is InvalidOperationException)
            {
                _hasError = true;
            }
        }
    }
}
