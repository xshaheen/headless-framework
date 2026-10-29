// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// A graceful stop hands rows back with a write that can fail or match no row: the store faulted, or a peer reclaimed
/// the row first. Either way this node can no longer vouch for the row, so the run must end as lease loss: nothing
/// escapes to the pool, no later write or consumer callback runs, and reclaim settles the row.
/// </summary>
public sealed class JobsShutdownWriteOutcomeTests : TestBase
{
    // The event the handler logs when a stop-time hand-back throws.
    private const int _ShutdownWriteFailedEventId = 3127;

    // Only a failure guard: every scenario below ends on its own once the behavior holds.
    private static readonly TimeSpan _RunBound = TimeSpan.FromSeconds(10);

    public enum ShutdownWrite
    {
        Throws = 0,
        MatchesNoRow = 1,
    }

    [Theory]
    [InlineData(ShutdownWrite.Throws)]
    [InlineData(ShutdownWrite.MatchesNoRow)]
    public async Task should_end_as_lease_loss_when_the_drain_hand_back_of_an_unstarted_run_does_not_land(
        ShutdownWrite write
    )
    {
        // given: the pool drains before the run is picked up
        await using var fixture = Fixture.Create(write);
        var invoked = false;
        var context = fixture.Context(
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            }
        );
        fixture.Scheduler.BeginDrain();

        // when
        var run = () =>
            fixture
                .Handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken)
                .WaitAsync(_RunBound, AbortToken);

        // then
        await run.Should().NotThrowAsync();
        invoked.Should().BeFalse();
        context.LeaseLost.Should().BeTrue();
        await fixture.Manager.Received(1).ReleaseInProgressAsync(context, CancellationToken.None);
        await fixture.ShouldHaveWrittenNothingElseAsync();
        fixture.ShutdownWriteFailures.Should().Be(write == ShutdownWrite.Throws ? 1 : 0);
    }

    [Theory]
    [InlineData(ShutdownWrite.Throws)]
    [InlineData(ShutdownWrite.MatchesNoRow)]
    public async Task should_end_as_lease_loss_when_settling_a_run_the_stop_interrupted_does_not_land(
        ShutdownWrite write
    )
    {
        // given: a running job the stop cancels after the drain began
        await using var fixture = Fixture.Create(write);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = fixture.Context(
            async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
        );
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
#pragma warning disable AsyncFixer04 // False positive: the run is awaited below, before either resource is disposed.
        var execution = fixture.Handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: stopping.Token);
#pragma warning restore AsyncFixer04
        await started.Task.WaitAsync(_RunBound, AbortToken);

        // when
        fixture.Scheduler.BeginDrain();
        await stopping.CancelAsync();
        var run = () => execution.WaitAsync(_RunBound, AbortToken);

        // then
        await run.Should().NotThrowAsync();
        context.LeaseLost.Should().BeTrue();
        await fixture.Manager.Received(1).SettleInterruptedAsync(context, Arg.Any<TimeSpan?>(), CancellationToken.None);
        await fixture.ShouldHaveWrittenNothingElseAsync();
        fixture.ShutdownWriteFailures.Should().Be(write == ShutdownWrite.Throws ? 1 : 0);
    }

    [Theory]
    [InlineData(ShutdownWrite.Throws)]
    [InlineData(ShutdownWrite.MatchesNoRow)]
    public async Task should_end_as_lease_loss_when_the_hand_back_of_an_undispatched_run_does_not_land(
        ShutdownWrite write
    )
    {
        // given: a row claimed for an immediate run the frozen pool refused
        await using var fixture = Fixture.Create(write);
        var context = fixture.Context((_, _) => Task.CompletedTask);

        // when
        var release = () => fixture.Handler.ReleaseUndispatchedAsync(context).WaitAsync(_RunBound, AbortToken);

        // then: the dispatcher goes on to hand back the rest of its batch
        await release.Should().NotThrowAsync();
        context.LeaseLost.Should().BeTrue();
        await fixture.ShouldHaveWrittenNothingElseAsync();
        fixture.ShutdownWriteFailures.Should().Be(write == ShutdownWrite.Throws ? 1 : 0);
    }

    [Fact]
    public async Task should_end_as_lease_loss_when_the_hand_back_under_an_unknown_membership_matches_no_row()
    {
        // given: the node's coordination membership is not established, and a peer already reclaimed the row
        await using var fixture = Fixture.Create(ShutdownWrite.MatchesNoRow, identityKnown: false);
        var invoked = false;
        var context = fixture.Context(
            (_, _) =>
            {
                invoked = true;
                return Task.CompletedTask;
            }
        );

        // when
        await fixture
            .Handler.ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken)
            .WaitAsync(_RunBound, AbortToken);

        // then
        invoked.Should().BeFalse();
        context.LeaseLost.Should().BeTrue();
        await fixture.Manager.Received(1).ReleaseInProgressAsync(context, CancellationToken.None);
        await fixture.ShouldHaveWrittenNothingElseAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly EventCapturingLogger _logger;

        private Fixture(
            ServiceProvider services,
            IInternalJobManager manager,
            IJobExceptionHandler exceptionHandler,
            JobsTaskScheduler scheduler,
            EventCapturingLogger logger,
            JobsExecutionTaskHandler handler
        )
        {
            _services = services;
            Manager = manager;
            ExceptionHandler = exceptionHandler;
            Scheduler = scheduler;
            _logger = logger;
            Handler = handler;
        }

        public IInternalJobManager Manager { get; }

        public IJobExceptionHandler ExceptionHandler { get; }

        public JobsTaskScheduler Scheduler { get; }

        public JobsExecutionTaskHandler Handler { get; }

        public int ShutdownWriteFailures => _logger.Count(_ShutdownWriteFailedEventId);

        public static Fixture Create(ShutdownWrite write, bool identityKnown = true)
        {
            var manager = Substitute.For<IInternalJobManager>();
            manager
                .RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(1));
            manager
                .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<bool?>(false));
            var outcome =
                write == ShutdownWrite.Throws
                    ? Task.FromException<int>(new TimeoutException("store unreachable"))
                    : Task.FromResult(0);
            manager.ReleaseInProgressAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(outcome);
            manager
                .SettleInterruptedAsync(
                    Arg.Any<JobExecutionState>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(outcome);

            var exceptionHandler = Substitute.For<IJobExceptionHandler>();
            var registrations = new ServiceCollection();
            registrations.AddSingleton(manager);
            registrations.AddSingleton(exceptionHandler);
            var services = registrations.BuildServiceProvider();

            var identity = Substitute.For<IJobsOwnerIdentity>();
            identity.MembershipLostToken.Returns(CancellationToken.None);
            identity
                .TryGetStampOwner(out Arg.Any<string?>())
                .Returns(call =>
                {
                    call[0] = identityKnown ? "node-a@1" : null;
                    return identityKnown;
                });

            var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
            var logger = new EventCapturingLogger();
            var registry = JobFunctionRegistryBuilder.Build([], [], []);
            var handler = new JobsExecutionTaskHandler(
                services,
                TimeProvider.System,
                Substitute.For<IJobsInstrumentation>(),
                manager,
                registry,
                new JobsExecutionCancellationRegistry(),
                new SchedulerOptionsBuilder(),
                logger,
                new JobsFunctionTagBound(registry),
                taskScheduler: scheduler,
                ownerIdentity: identity
            );

            return new Fixture(services, manager, exceptionHandler, scheduler, logger, handler);
        }

        public JobExecutionState Context(Func<JobContext, CancellationToken, Task> body)
        {
            return new JobExecutionState
            {
                JobId = Guid.NewGuid(),
                FunctionName = "shutdown.write",
                Type = JobType.TimeJob,
                ExecutionTime = DateTime.UtcNow,
                RetryIntervals = [0],
                Retries = 3,
                RetryCount = 0,
                Status = JobStatus.InProgress,
                ClaimOwner = "node-a@1",
                CachedDelegate = (_, execution, token) => body(execution, token),
            };
        }

        // A hand-back that did not land leaves the row as the store holds it: no status write, no chain processing,
        // and no consumer callback that would report an outcome nobody recorded.
        public async Task ShouldHaveWrittenNothingElseAsync()
        {
            await Manager
                .DidNotReceiveWithAnyArgs()
                .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>());
            await Manager
                .DidNotReceiveWithAnyArgs()
                .ApplyParentTerminalRunConditionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
            await ExceptionHandler
                .DidNotReceiveWithAnyArgs()
                .HandleCanceledExceptionAsync(default!, default, default, default);
            await ExceptionHandler.DidNotReceiveWithAnyArgs().HandleExceptionAsync(default!, default, default, default);
        }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class EventCapturingLogger : ILogger<JobsExecutionTaskHandler>
    {
        private readonly Lock _gate = new();
        private readonly List<int> _eventIds = [];

        public int Count(int eventId)
        {
            lock (_gate)
            {
                return _eventIds.Count(id => id == eventId);
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (_gate)
            {
                _eventIds.Add(eventId.Id);
            }
        }
    }
}
