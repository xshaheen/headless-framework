// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data.Common;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Native claim scopes retry a transient fault raised before their commit as a whole, in a fresh transaction, on every
/// engine, and never retry a fault raised by the commit. Each provider supplies the exception its driver raises for a
/// deadlock victim.
/// </summary>
public abstract class JobsClaimRetryConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _ClaimRetryEventName = "JobsClaimTransientRetry";

    /// <summary>Creates the exception the provider's driver raises when the database picks a deadlock victim.</summary>
    protected abstract Exception CreateTransientClaimFailure();

    /// <summary>Returns whether <paramref name="exception" /> is the failure <see cref="CreateTransientClaimFailure" /> made.</summary>
    protected abstract bool IsInjectedFailure(Exception exception);

    public virtual async Task transient_fault_before_commit_is_retried_and_commits_correct_durable_state()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var fault = new ClaimFaultInterceptor(ClaimFaultPoint.Begin, failuresToInject: 1, CreateTransientClaimFailure);
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildNativeClaimHost("deadlock-retry-a", fault, logs);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "deadlock-retry",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([job], ct);
            fault.Arm();

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            // The retry path really ran: the first scope failed, the second committed.
            fault.InjectedFailureCount.Should().Be(1);
            fault.AttemptCount.Should().Be(2);
            logs.CountOf(_ClaimRetryEventName).Should().Be(1);
            claimed.Should().ContainSingle().Which.Id.Should().Be(job.Id);
            claimed[0].OwnerId.Should().NotBeNullOrWhiteSpace();
            var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(job.Id, ct);
            status.Should().Be((int)JobStatus.Queued);
            ownerId.Should().Be(claimed[0].OwnerId);
            lockedUntil.Should().NotBeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task transient_retries_are_bounded_and_the_driver_exception_propagates()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var fault = new ClaimFaultInterceptor(
            ClaimFaultPoint.Begin,
            failuresToInject: int.MaxValue,
            CreateTransientClaimFailure
        );
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildNativeClaimHost("deadlock-retry-b", fault, logs);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "deadlock-exhausted",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([job], ct);
            fault.Arm();

            var claim = async () => await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            IsInjectedFailure((await claim.Should().ThrowAsync<Exception>()).Which).Should().BeTrue();
            // One initial attempt plus the strategy's two retries — the budget is bounded, not infinite.
            fault.InjectedFailureCount.Should().Be(3);
            logs.CountOf(_ClaimRetryEventName).Should().Be(2);
            var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(job.Id, ct);
            status.Should().Be((int)JobStatus.Idle);
            ownerId.Should().BeNull();
            lockedUntil.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task transient_fault_from_the_commit_is_not_retried_and_the_driver_exception_propagates()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var fault = new ClaimFaultInterceptor(
            ClaimFaultPoint.Commit,
            failuresToInject: int.MaxValue,
            CreateTransientClaimFailure
        );
        using var logs = new CapturingLoggerProvider();
        using var host = _BuildNativeClaimHost("commit-fault", fault, logs);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var job = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "commit-fault",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
            };
            await persistence.AddTimeJobsAsync([job], ct);
            fault.Arm();

            var claim = async () => await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct);

            // A commit fault may come from a claim the database already made durable, so it is never retried, even
            // when the driver calls it transient.
            IsInjectedFailure((await claim.Should().ThrowAsync<Exception>()).Which).Should().BeTrue();
            fault.InjectedFailureCount.Should().Be(1);
            logs.CountOf(_ClaimRetryEventName).Should().Be(0);
            // The injected fault fires before the real commit, so the claim rolled back.
            var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(job.Id, ct);
            status.Should().Be((int)JobStatus.Idle);
            ownerId.Should().BeNull();
            lockedUntil.Should().BeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// Mirrors the harness host wiring but keeps native SQL Server claiming ON while attaching an interceptor and a
    /// log sink. <see cref="JobsCoordinationFixtureExtensions.BuildInterceptedHost" /> cannot be reused here: it
    /// deliberately turns native claiming off, and the native claim scope is exactly what is under test.
    /// </summary>
    private IHost _BuildNativeClaimHost(string nodeId, IInterceptor interceptor, ILoggerProvider logs)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddProvider(logs);

        builder.Services.AddHeadlessHostIdentity(options => options.HostName = nodeId);
        builder.Services.AddHeadlessCoordination(setup =>
        {
            fixture.ConfigureCoordination(setup);
            setup.Configure(options =>
            {
                options.ClusterName = JobsCoordinationFixtureExtensions.ClusterName;
                options.HeartbeatInterval = JobsCoordinationFixtureExtensions.HeartbeatInterval;
                options.SuspicionThreshold = JobsCoordinationFixtureExtensions.SuspicionThreshold;
                options.DeadThreshold = JobsCoordinationFixtureExtensions.DeadThreshold;
                options.DeadRetentionWindow = JobsCoordinationFixtureExtensions.DeadRetentionWindow;
                options.MembershipLostBehavior = MembershipLostBehavior.StopMembershipOnly;
            });
        });

        builder.Services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.AddModule<CoordinatedJobsModule>();
            options.UseEntityFramework(ef =>
            {
                ef.UseJobsDbContext<JobsDbContext>(db =>
                {
                    fixture.ConfigureStore(db);
                    db.AddInterceptors(interceptor);
                });
                fixture.ConfigureClaims(ef);
            });
        });

        return builder.Build();
    }
}

/// <summary>Where <see cref="ClaimFaultInterceptor" /> fails the claim scope.</summary>
public enum ClaimFaultPoint
{
    /// <summary>The claim transaction's begin: before the commit, so the scope is retried.</summary>
    Begin = 0,

    /// <summary>The claim transaction's commit: the scope is never retried.</summary>
    Commit = 1,
}

/// <summary>
/// Fails the claim scope with the provider's own transient exception, at the EF transaction begin or commit rather
/// than in a <c>DbCommandInterceptor</c>, because the native claim statements are raw ADO commands built off the
/// underlying connection and never reach EF's command interception pipeline. The begin and the commit are the first
/// and last EF-observable steps inside the retried scope, so a failure at the begin discards the attempt the way a
/// fault in its statements does.
/// </summary>
public sealed class ClaimFaultInterceptor(ClaimFaultPoint point, int failuresToInject, Func<Exception> createFailure)
    : DbCommandInterceptor,
        IDbTransactionInterceptor
{
    // Transactions that carried an EF-issued command. The native claim scope issues none — it builds raw ADO
    // commands off the underlying connection — so this is what separates a claim commit from an unrelated EF
    // write (the dead-owner reclaimer's ExecuteUpdate, seeding, coordination bookkeeping) sharing the host. Npgsql
    // reuses one transaction object per pooled connection, so the mark is cleared whenever a transaction starts.
    private readonly ConcurrentDictionary<DbTransaction, byte> _efTouchedTransactions = new();
    private int _armed;
    private int _attempts;
    private int _injectedFailures;

    /// <summary>
    /// Claim-scope begins or commits (per <see cref="ClaimFaultPoint" />) observed after <see cref="Arm" />,
    /// including the ones that were failed.
    /// </summary>
    public int AttemptCount => Volatile.Read(ref _attempts);

    /// <summary>Injected failures actually thrown — proves the retry path was exercised, not skipped.</summary>
    public int InjectedFailureCount => Volatile.Read(ref _injectedFailures);

    /// <summary>Starts faulting; called after seeding so setup writes commit normally.</summary>
    public void Arm() => Interlocked.Exchange(ref _armed, 1);

    public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default
    )
    {
        // Only the claim scope begins an EF transaction once the test arms the interceptor: the other EF work the
        // host runs is single-statement and implicit-transaction.
        if (point == ClaimFaultPoint.Begin)
        {
            _InjectIfArmed();
        }

        return ValueTask.FromResult(result);
    }

    public DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result
    )
    {
        _efTouchedTransactions.TryRemove(result, out _);

        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        _efTouchedTransactions.TryRemove(result, out _);

        return ValueTask.FromResult(result);
    }

    public ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (point == ClaimFaultPoint.Commit && !_efTouchedTransactions.ContainsKey(transaction))
        {
            _InjectIfArmed();
        }

        return ValueTask.FromResult(result);
    }

    private void _InjectIfArmed()
    {
        if (Volatile.Read(ref _armed) == 1 && Interlocked.Increment(ref _attempts) <= failuresToInject)
        {
            Interlocked.Increment(ref _injectedFailures);

            throw createFailure();
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        _TrackTransaction(command);

        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        _TrackTransaction(command);

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        _TrackTransaction(command);

        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        _TrackTransaction(command);

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        _TrackTransaction(command);

        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        _TrackTransaction(command);

        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void _TrackTransaction(DbCommand command)
    {
        if (command.Transaction is { } transaction)
        {
            _efTouchedTransactions.TryAdd(transaction, 0);
        }
    }
}

/// <summary>Captures the event names of emitted log entries so a test can assert on retry observability.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _eventNames = new();

    public int CountOf(string eventName) =>
        _eventNames.Count(name => string.Equals(name, eventName, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_eventNames);

    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> eventNames) : ILogger
    {
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
            if (!string.IsNullOrEmpty(eventId.Name))
            {
                eventNames.Enqueue(eventId.Name);
            }
        }
    }
}
