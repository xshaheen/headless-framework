// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Jobs;
using Headless.Jobs.DependencyInjection;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests.Transactions;

/// <summary>
/// Unit coverage for <see cref="JobsManager{TTimeJob,TCronJob}" /> commit-coordination routing: the synchronous
/// capture fork, the fail-loud cases, and post-commit side-effect deferral. Atomicity itself (rows committing /
/// discarding with the caller's transaction) is integration-only — see the EF harness conformance suite.
/// </summary>
public sealed class JobsManagerCoordinatedRoutingTests
{
    private const string FunctionName = "routing-test-fn";

    static JobsManagerCoordinatedRoutingTests()
    {
        // AddHeadlessJobs normally seeds this from the scheduler options; the manager's cron-expression validation
        // needs it set or GetNextOccurrenceOrDefault returns null and AddAsync reports a parse failure.
        CronScheduleCache.TimeZoneInfo = TimeZoneInfo.Utc;

        // The manager validates the function exists before routing. No other unit test mutates JobFunctionProvider, so
        // a one-time static registration is stable for this assembly.
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, (string, JobPriority, JobFunctionDelegate, int)>(StringComparer.Ordinal)
            {
                [FunctionName] = ("0 0 * * *", JobPriority.LongRunning, (_, _, _) => Task.CompletedTask, 1),
            }
        );
        JobFunctionProvider.Build();
    }

    [Fact]
    public async Task TimeJob_without_coordinator_takes_direct_path()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);

        var result = await sut.Time.AddAsync(_FutureTimeJob(), TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        await sut.Persistence.Received(1).AddTimeJobs(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.Received(1).AddTimeJobNotifyAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task TimeJob_coordinator_without_relational_capability_takes_direct_path()
    {
        // A messaging-only coordinated scope: coordination must not become infectious — fall back to direct insert.
        var sut = _CreateSut(CoordinatorMode.NonRelational, withWriter: true);

        var result = await sut.Time.AddAsync(_FutureTimeJob(), TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        await sut.Persistence.Received(1).AddTimeJobs(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Coordinator!.OnCommitCount.Should().Be(0);
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TimeJob_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);

        var act = () => sut.Time.AddAsync(_FutureTimeJob(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().AddTimeJobs(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TimeJob_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        // Live relational coordinator, but the provider cannot write inside the ambient transaction (in-memory shape).
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);

        var act = () => sut.Time.AddAsync(_FutureTimeJob(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().AddTimeJobs(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cron_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);

        var act = () => sut.Cron.AddAsync(_CronJob(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().InsertCronJobs(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Cron_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);

        var act = () => sut.Cron.AddAsync(_CronJob(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().InsertCronJobs(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TimeJob_batch_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);
        var jobs = new List<TimeJobEntity> { _FutureTimeJob(), _FutureTimeJob() };

        var act = () => sut.Time.AddBatchAsync(jobs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().AddTimeJobs(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteTimeJobsAsync(
                Arg.Any<TimeJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TimeJob_batch_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);
        var jobs = new List<TimeJobEntity> { _FutureTimeJob(), _FutureTimeJob() };

        var act = () => sut.Time.AddBatchAsync(jobs, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().AddTimeJobs(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cron_batch_dead_transaction_throws_and_persists_nothing()
    {
        var sut = _CreateSut(CoordinatorMode.DeadRelational, withWriter: true);
        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };

        var act = () => sut.Cron.AddBatchAsync(crons, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().InsertCronJobs(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        await sut
            .Writer.DidNotReceive()
            .WriteCronJobsAsync(
                Arg.Any<CronJobEntity[]>(),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Cron_batch_relational_coordinator_but_non_coordinated_provider_throws_mis_wire()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: false);
        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };

        var act = () => sut.Cron.AddBatchAsync(crons, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await sut.Persistence.DidNotReceive().InsertCronJobs(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TimeJob_live_coordinator_writes_in_transaction_and_defers_side_effects()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var job = _FutureTimeJob();

        var result = await sut.Time.AddAsync(job, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(job);
        await sut
            .Writer.Received(1)
            .WriteTimeJobsAsync(
                Arg.Is<TimeJobEntity[]>(a => a.Length == 1 && a[0].Id == job.Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Side effects must NOT have fired synchronously and the row must NOT have gone through the direct insert.
        await sut.Persistence.DidNotReceive().AddTimeJobs(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddTimeJobNotifyAsync(Arg.Any<Guid>());

        await sut.Coordinator.DrainCommitAsync(TestContext.Current.CancellationToken);

        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.Received(1).AddTimeJobNotifyAsync(job.Id);
    }

    [Fact]
    public async Task Deferred_side_effect_failure_is_swallowed_and_logged()
    {
        // KTD-4 crash isolation: once the row is durably committed, a deferred post-commit side-effect failure must
        // NOT propagate out of the commit drain (which would surface as a caller error after a successful commit) —
        // it is swallowed and logged against the job scope so the polling sweep can recover.
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var boom = new InvalidOperationException("deferred side effect boom");
        sut.Notification.AddTimeJobNotifyAsync(Arg.Any<Guid>()).Returns(Task.FromException(boom));

        await sut.Time.AddAsync(_FutureTimeJob(), TestContext.Current.CancellationToken);

        var drain = () => sut.Coordinator!.DrainCommitAsync(TestContext.Current.CancellationToken);
        await drain.Should().NotThrowAsync();

        sut.Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && ReferenceEquals(e.Exception, boom));
    }

    // Note: the former shutdown-cancellation test was removed with the OCE-on-shutdown branch it covered. The drain
    // now bounds side effects with its own timeout token (the coordinator always drains with CancellationToken.None),
    // so a deferred failure is exercised by Deferred_side_effect_failure_is_swallowed_and_logged above. The deferred
    // timeout path mirrors MessageOutboxBuffer's bounded flush; a deterministic timeout test is a follow-up.

    [Fact]
    public async Task Coordinated_enqueue_registers_no_rollback_callbacks()
    {
        // Coordinated side effects must fire only on commit — a rollback discards the row, so the manager must
        // never register a rollback callback (which would run side effects for work that was rolled back).
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);

        await sut.Time.AddAsync(_FutureTimeJob(), TestContext.Current.CancellationToken);

        sut.Coordinator!.OnCommitCount.Should().Be(1);
        sut.Coordinator.OnRollbackCount.Should().Be(0);
    }

    [Fact]
    public async Task TimeJob_live_coordinator_defers_immediate_dispatch_to_commit()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true, dispatcherEnabled: true);
        sut.Persistence.AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>()).Returns([]);

        await sut.Time.AddAsync(_ImmediateTimeJob(), TestContext.Current.CancellationToken);

        // The immediate-acquire probe is part of the deferred side effects, not the synchronous enqueue.
        await sut
            .Persistence.DidNotReceive()
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());

        await sut.Coordinator!.DrainCommitAsync(TestContext.Current.CancellationToken);

        await sut
            .Persistence.Received(1)
            .AcquireImmediateTimeJobsAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TimeJob_batch_live_coordinator_routes_all_and_defers_side_effects_once()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var jobs = new List<TimeJobEntity> { _FutureTimeJob(), _FutureTimeJob() };

        var result = await sut.Time.AddBatchAsync(jobs, TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        // R3: the batch reaches the seam as one array in insertion order (AddRange preserves it downstream).
        await sut
            .Writer.Received(1)
            .WriteTimeJobsAsync(
                Arg.Is<TimeJobEntity[]>(a => a.Length == 2 && a[0].Id == jobs[0].Id && a[1].Id == jobs[1].Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);
        await sut.Notification.DidNotReceive().AddTimeJobsBatchNotifyAsync();

        await sut.Coordinator.DrainCommitAsync(TestContext.Current.CancellationToken);

        await sut.Notification.Received(1).AddTimeJobsBatchNotifyAsync();
    }

    [Fact]
    public async Task Cron_without_coordinator_takes_direct_path()
    {
        var sut = _CreateSut(CoordinatorMode.None, withWriter: false);

        var result = await sut.Cron.AddAsync(_CronJob(), TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        await sut.Persistence.Received(1).InsertCronJobs(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cron_live_coordinator_writes_in_transaction_and_defers_cache_invalidation()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var cron = _CronJob();

        var result = await sut.Cron.AddAsync(cron, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(cron);
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(a => a.Length == 1 && a[0].Id == cron.Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Cache invalidation + scheduler + notify must be deferred — never on a pre-commit snapshot.
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        await sut.Persistence.DidNotReceive().InsertCronJobs(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());

        await sut.Coordinator.DrainCommitAsync(TestContext.Current.CancellationToken);

        await sut.Writer.Received(1).InvalidateCronExpressionsCacheAsync();
        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.Received(1).AddCronJobNotifyAsync(cron);
    }

    [Fact]
    public async Task Cron_batch_live_coordinator_routes_all_and_defers_side_effects_once()
    {
        var sut = _CreateSut(CoordinatorMode.LiveRelational, withWriter: true);
        var crons = new List<CronJobEntity> { _CronJob(), _CronJob() };

        var result = await sut.Cron.AddBatchAsync(crons, TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        // R3: the batch reaches the seam as one array in insertion order (AddRange preserves it downstream).
        await sut
            .Writer.Received(1)
            .WriteCronJobsAsync(
                Arg.Is<CronJobEntity[]>(a => a.Length == 2 && a[0].Id == crons[0].Id && a[1].Id == crons[1].Id),
                Arg.Any<IRelationalCommitContext>(),
                Arg.Any<CancellationToken>()
            );
        sut.Coordinator!.OnCommitCount.Should().Be(1);

        // Cache invalidation + scheduler + per-entity notify must be deferred to commit, never on a pre-commit snapshot.
        await sut.Writer.DidNotReceive().InvalidateCronExpressionsCacheAsync();
        await sut.Persistence.DidNotReceive().InsertCronJobs(Arg.Any<CronJobEntity[]>(), Arg.Any<CancellationToken>());
        sut.Scheduler.DidNotReceive().RestartIfNeeded(Arg.Any<DateTime>());
        await sut.Notification.DidNotReceive().AddCronJobNotifyAsync(Arg.Any<CronJobEntity>());

        await sut.Coordinator.DrainCommitAsync(TestContext.Current.CancellationToken);

        // Cache invalidation fires exactly once for the batch; scheduler restarts once; notify fires per entity.
        await sut.Writer.Received(1).InvalidateCronExpressionsCacheAsync();
        sut.Scheduler.Received(1).RestartIfNeeded(Arg.Any<DateTime>());
        foreach (var cron in crons)
        {
            await sut.Notification.Received(1).AddCronJobNotifyAsync(cron);
        }
    }

    [Fact]
    public void Jobs_only_host_resolves_manager_with_null_coordinator_fallback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        using var provider = services.BuildServiceProvider();

        provider.GetService<ITimeJobManager<TimeJobEntity>>().Should().NotBeNull();
        provider.GetRequiredService<ICurrentCommitCoordinator>().Current.Should().BeNull();
    }

    private static TimeJobEntity _FutureTimeJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = FunctionName,
            Description = FunctionName,
            Request = [],
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };

    private static TimeJobEntity _ImmediateTimeJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = FunctionName,
            Description = FunctionName,
            Request = [],
            ExecutionTime = DateTime.UtcNow,
        };

    private static CronJobEntity _CronJob() =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = FunctionName,
            Description = FunctionName,
            // CronScheduleCache parses with IncludingSeconds = true, so the expression has six fields.
            Expression = "0 0 0 * * *",
            Request = [],
        };

    private enum CoordinatorMode
    {
        None,
        NonRelational,
        LiveRelational,
        DeadRelational,
    }

    private static Sut _CreateSut(CoordinatorMode mode, bool withWriter, bool dispatcherEnabled = false)
    {
        var persistence = withWriter
            ? Substitute.For<
                IJobPersistenceProvider<TimeJobEntity, CronJobEntity>,
                ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>
            >()
            : Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var scheduler = Substitute.For<IJobsHostScheduler>();
        var notification = Substitute.For<IJobsNotificationHubSender>();
        var dispatcher = Substitute.For<IJobsDispatcher>();
        dispatcher.IsEnabled.Returns(dispatcherEnabled);

        FakeCommitCoordinator? coordinator = mode switch
        {
            CoordinatorMode.None => null,
            CoordinatorMode.NonRelational => new FakeCommitCoordinator(relational: null),
            CoordinatorMode.LiveRelational => new FakeCommitCoordinator(
                new FakeRelationalCommitContext(Substitute.For<DbTransaction>())
            ),
            CoordinatorMode.DeadRelational => new FakeCommitCoordinator(
                new FakeRelationalCommitContext(transaction: null)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        var logger = new CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>>();

        var manager = new JobsManager<TimeJobEntity, CronJobEntity>(
            persistence,
            scheduler,
            TimeProvider.System,
            notification,
            new JobsExecutionContext(),
            dispatcher,
            new FakeCurrentCommitCoordinator(coordinator),
            logger
        );

        return new Sut
        {
            Persistence = persistence,
            Scheduler = scheduler,
            Notification = notification,
            Dispatcher = dispatcher,
            Coordinator = coordinator,
            Manager = manager,
            Logger = logger,
        };
    }

    private sealed class Sut
    {
        public required IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Persistence { get; init; }
        public required IJobsHostScheduler Scheduler { get; init; }
        public required IJobsNotificationHubSender Notification { get; init; }
        public required IJobsDispatcher Dispatcher { get; init; }
        public required FakeCommitCoordinator? Coordinator { get; init; }
        public required JobsManager<TimeJobEntity, CronJobEntity> Manager { get; init; }
        public required CapturingLogger<JobsManager<TimeJobEntity, CronJobEntity>> Logger { get; init; }

        public ITimeJobManager<TimeJobEntity> Time => Manager;

        public ICronJobManager<CronJobEntity> Cron => Manager;

        public ICoordinatedJobWriter<TimeJobEntity, CronJobEntity> Writer =>
            (ICoordinatedJobWriter<TimeJobEntity, CronJobEntity>)Persistence;
    }

    private sealed class FakeCurrentCommitCoordinator(ICommitCoordinator? current) : ICurrentCommitCoordinator
    {
        public ICommitCoordinator? Current { get; } = current;
    }

    private sealed class FakeRelationalCommitContext(DbTransaction? transaction) : IRelationalCommitContext
    {
        public DbConnection? Connection => Transaction?.Connection;

        public DbTransaction? Transaction { get; } = transaction;
    }

    // Captures registered OnCommit callbacks so a test can assert deferral, then drive them to assert the deferred
    // work fires post-commit. Mirrors the real coordinator's "register now, drain after commit" contract.
    private sealed class FakeCommitCoordinator(IRelationalCommitContext? relational) : ICommitCoordinator
    {
        private readonly List<Func<CommitContext, CancellationToken, ValueTask>> _onCommit = [];
        private readonly List<Func<CommitContext, CancellationToken, ValueTask>> _onRollback = [];

        public int OnCommitCount => _onCommit.Count;

        public int OnRollbackCount => _onRollback.Count;

        public CommitCoordinatorState State => CommitCoordinatorState.Active;

        public IDisposable OnCommit(Func<CommitContext, CancellationToken, ValueTask> work)
        {
            _onCommit.Add(work);

            return _NoopDisposable.Instance;
        }

        public IDisposable OnRollback(Func<CommitContext, CancellationToken, ValueTask> work)
        {
            _onRollback.Add(work);

            return _NoopDisposable.Instance;
        }

        public TBuffer GetOrAdd<TBuffer>(Func<ICommitCoordinator, TBuffer> factory)
            where TBuffer : class, ICommitWorkBuffer => factory(this);

        public TBuffer GetOrAdd<TBuffer, TState>(TState state, Func<ICommitCoordinator, TState, TBuffer> factory)
            where TBuffer : class, ICommitWorkBuffer => factory(this, state);

        public bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
            where TCapability : class, ICommitCapability
        {
            if (relational is TCapability typed)
            {
                capability = typed;

                return true;
            }

            capability = null;

            return false;
        }

        public async Task DrainCommitAsync(CancellationToken cancellationToken)
        {
            var context = new CommitContext
            {
                Services = _EmptyServiceProvider.Instance,
                Outcome = CommitOutcome.Committed,
            };

            foreach (var work in _onCommit)
            {
                await work(context, cancellationToken);
            }
        }

        private sealed class _NoopDisposable : IDisposable
        {
            public static readonly _NoopDisposable Instance = new();

            public void Dispose() { }
        }
    }

    private sealed class _EmptyServiceProvider : IServiceProvider
    {
        public static readonly _EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    // Records the LoggerMessage-emitted entries so a test can assert the deferred-failure log without a logging mock.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => _NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, exception));

        private sealed class _NullScope : IDisposable
        {
            public static readonly _NullScope Instance = new();

            public void Dispose() { }
        }
    }
}
