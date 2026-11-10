// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Fencing;
using Headless.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

// A graceful host stop hands a job back without spending its retry budget: a run that never started returns to Idle,
// and an interrupted run is settled by its node-death policy. Neither write may touch a row a peer already reclaimed.
public abstract partial class JobsCoordinationConformanceTests<TFixture>
{
    private const string _StopOwner = "node-a@1";
    private const string _StopPeer = "node-b@1";
    private const string _StopReason = "interrupted by host shutdown";

    public virtual async Task graceful_release_returns_a_running_job_to_idle_uncharged_at_its_stored_instant()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var id = Guid.NewGuid();
        await Fixture.SeedTimeJobAsync(
            id,
            "GracefulRelease",
            (int)JobStatus.InProgress,
            _StopOwner,
            ct,
            NodeDeathPolicy.MarkFailed,
            lockedUntil: DateTime.UtcNow.AddMinutes(5)
        );
        var before = await persistence.GetTimeJobByIdAsync(id, ct);

        (await persistence.ReleaseInProgressTimeJobAsync(JobsLeaseTables.TimeJob(id, 1), ct)).Should().Be(1);

        // The release ignores the policy: the job never ran.
        var (status, lease, _, _) = await Fixture.ReadTimeJobDetailAsync(id, ct);
        status.Should().Be((int)JobStatus.Idle);
        lease.State.Should().NotBe(RowLeaseState.Active);
        (await Fixture.ReadTimeJobRetryCountAsync(id, ct)).Should().Be(0);
        (await persistence.GetTimeJobByIdAsync(id, ct))!.ExecutionTime.Should().Be(before!.ExecutionTime);
    }

    public virtual async Task graceful_settlement_applies_the_node_death_policy_without_charging(int policy)
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        // The node lags the database by two minutes, so a due time it computed itself would miss the tolerance below:
        // the settle runs as a fenced raw command, so the stored due time is the evidence that the database set it.
        using var host = Fixture.BuildHost("node-a", timeProvider: new SkewedTimeProvider(TimeSpan.FromMinutes(-2)));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var nodeDeathPolicy = (NodeDeathPolicy)policy;
        var id = Guid.NewGuid();
        await Fixture.SeedTimeJobAsync(
            id,
            "GracefulSettle",
            (int)JobStatus.InProgress,
            _StopOwner,
            ct,
            nodeDeathPolicy,
            lockedUntil: DateTime.UtcNow.AddMinutes(5)
        );
        var before = await persistence.GetTimeJobByIdAsync(id, ct);
        var remaining = TimeSpan.FromSeconds(20);

        var storeBefore = await _StoreUtcNowAsync(ct);
        (await persistence.SettleInterruptedTimeJobAsync(JobsLeaseTables.TimeJob(id, 1), _StopReason, remaining, ct))
            .Should()
            .Be(1);
        var storeAfter = await _StoreUtcNowAsync(ct);

        var (status, lease, exceptionMessage, skippedReason) = await Fixture.ReadTimeJobDetailAsync(id, ct);
        var stored = (await persistence.GetTimeJobByIdAsync(id, ct))!;
        (await Fixture.ReadTimeJobRetryCountAsync(id, ct)).Should().Be(0);
        lease.State.Should().NotBe(RowLeaseState.Active);

        switch (nodeDeathPolicy)
        {
            case NodeDeathPolicy.Retry:
                status.Should().Be((int)JobStatus.Idle);
                stored
                    .ExecutionTime.Should()
                    .BeOnOrAfter(storeBefore + remaining - TimeSpan.FromSeconds(1))
                    .And.BeOnOrBefore(storeAfter + remaining + TimeSpan.FromSeconds(1));
                break;
            case NodeDeathPolicy.MarkFailed:
                status.Should().Be((int)JobStatus.Failed);
                lease.Holder.Should().Be(_StopOwner);
                exceptionMessage.Should().Be(_StopReason);
                stored.ExecutionTime.Should().Be(before!.ExecutionTime);
                break;
            default:
                status.Should().Be((int)JobStatus.Skipped);
                lease.Holder.Should().Be(_StopOwner);
                skippedReason.Should().Be(_StopReason);
                stored.ExecutionTime.Should().Be(before!.ExecutionTime);
                break;
        }
    }

    public virtual async Task graceful_release_and_settlement_leave_a_row_a_peer_reclaimed_untouched()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var reclaimed = Guid.NewGuid();
        var notStarted = Guid.NewGuid();
        var lockedUntil = DateTime.UtcNow.AddMinutes(5);
        await Fixture.SeedTimeJobAsync(
            reclaimed,
            "GracefulFence",
            (int)JobStatus.InProgress,
            _StopPeer,
            ct,
            lockedUntil: lockedUntil
        );
        // The peer's takeover drew the next generation, so this node's attempt still holds the earlier one.
        await Fixture.OverwriteLeaseAsync(
            Fixture.QualifiedTimeJobsTable,
            reclaimed,
            new RowLeaseSnapshot(2, RowLeaseState.Active, new DateTimeOffset(lockedUntil, TimeSpan.Zero), _StopPeer, 1),
            ct
        );
        await Fixture.SeedTimeJobAsync(
            notStarted,
            "GracefulFence",
            (int)JobStatus.Queued,
            _StopOwner,
            ct,
            lockedUntil: lockedUntil
        );

        foreach (var id in new[] { reclaimed, notStarted })
        {
            (await persistence.ReleaseInProgressTimeJobAsync(JobsLeaseTables.TimeJob(id, 1), ct)).Should().Be(0);
            (await persistence.SettleInterruptedTimeJobAsync(JobsLeaseTables.TimeJob(id, 1), _StopReason, null, ct))
                .Should()
                .Be(0);
        }

        (await Fixture.ReadTimeJobAsync(reclaimed, ct)).Should().Be(((int)JobStatus.InProgress, _StopPeer));
        (await Fixture.ReadTimeJobAsync(notStarted, ct)).Should().Be(((int)JobStatus.Queued, _StopOwner));
    }

    public virtual async Task retry_reschedule_charges_the_attempt_and_waits_at_the_store_clock_under_any_policy(
        int policy
    )
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        // The node lags the database by two minutes, so a due time it computed itself would miss the tolerance below:
        // the reschedule runs as a fenced raw command, so the stored due time is the evidence that the database set it.
        using var host = Fixture.BuildHost("node-a", timeProvider: new SkewedTimeProvider(TimeSpan.FromMinutes(-2)));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var id = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        await Fixture.SeedTimeJobAsync(
            id,
            "RetryReschedule",
            (int)JobStatus.InProgress,
            _StopOwner,
            ct,
            (NodeDeathPolicy)policy,
            lockedUntil: DateTime.UtcNow.AddMinutes(5)
        );
        var peerLockedUntil = DateTime.UtcNow.AddMinutes(5);
        await Fixture.SeedTimeJobAsync(
            peerId,
            "RetryReschedule",
            (int)JobStatus.InProgress,
            _StopPeer,
            ct,
            lockedUntil: peerLockedUntil
        );
        // The peer's takeover drew the next generation, so this node's attempt still holds the earlier one.
        await Fixture.OverwriteLeaseAsync(
            Fixture.QualifiedTimeJobsTable,
            peerId,
            new RowLeaseSnapshot(
                2,
                RowLeaseState.Active,
                new DateTimeOffset(peerLockedUntil, TimeSpan.Zero),
                _StopPeer,
                1
            ),
            ct
        );
        var delay = TimeSpan.FromSeconds(60);

        var storeBefore = await _StoreUtcNowAsync(ct);
        (await persistence.RescheduleTimeJobRetryAsync(JobsLeaseTables.TimeJob(id, 1), 2, delay, ct)).Should().Be(1);
        var storeAfter = await _StoreUtcNowAsync(ct);

        // The node-death policy plays no part: the run failed with budget left, it was not interrupted.
        var (status, lease, _, _) = await Fixture.ReadTimeJobDetailAsync(id, ct);
        status.Should().Be((int)JobStatus.Idle);
        lease.State.Should().NotBe(RowLeaseState.Active);
        (await Fixture.ReadTimeJobRetryCountAsync(id, ct)).Should().Be(2);
        (await persistence.GetTimeJobByIdAsync(id, ct))!
            .ExecutionTime.Should()
            .BeOnOrAfter(storeBefore + delay - TimeSpan.FromSeconds(1))
            .And.BeOnOrBefore(storeAfter + delay + TimeSpan.FromSeconds(1));

        // A row a peer reclaimed, and a repeat of the landed write, match nothing.
        (await persistence.RescheduleTimeJobRetryAsync(JobsLeaseTables.TimeJob(peerId, 1), 1, delay, ct))
            .Should()
            .Be(0);
        (await persistence.RescheduleTimeJobRetryAsync(JobsLeaseTables.TimeJob(id, 1), 3, delay, ct)).Should().Be(0);
        (await Fixture.ReadTimeJobAsync(peerId, ct)).Should().Be(((int)JobStatus.InProgress, _StopPeer));
        (await Fixture.ReadTimeJobRetryCountAsync(id, ct)).Should().Be(2);
    }

    public virtual async Task graceful_settlement_of_a_cron_occurrence_applies_its_policy_without_charging(int policy)
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var nodeDeathPolicy = (NodeDeathPolicy)policy;
        var cronId = Guid.NewGuid();
        var occurrenceId = Guid.NewGuid();
        var peerOccurrenceId = Guid.NewGuid();
        var executionTime = DateTime.UtcNow.AddMinutes(-1);
        await Fixture.SeedCronJobAsync(cronId, "GracefulCron", "* * * * *", nodeDeathPolicy, ct);
        await Fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            cronId,
            (int)JobStatus.InProgress,
            _StopOwner,
            nodeDeathPolicy,
            DateTime.UtcNow.AddMinutes(5),
            executionTime,
            ct,
            disposition: CronOccurrenceDisposition.ReplacementOwed
        );
        await Fixture.SeedCronOccurrenceAsync(
            peerOccurrenceId,
            cronId,
            (int)JobStatus.InProgress,
            _StopPeer,
            nodeDeathPolicy,
            DateTime.UtcNow.AddMinutes(5),
            executionTime.AddMinutes(-1),
            ct
        );
        // The peer took the occurrence over at the next generation; this node's attempt held the earlier one.
        await Fixture.OverwriteLeaseAsync(
            Fixture.QualifiedCronJobOccurrencesTable,
            peerOccurrenceId,
            new RowLeaseSnapshot(2, RowLeaseState.Active, DateTimeOffset.UtcNow.AddMinutes(5), _StopPeer, 1),
            ct
        );

        (
            await persistence.SettleInterruptedCronJobOccurrenceAsync(
                JobsLeaseTables.CronOccurrence(occurrenceId, 1),
                _StopReason,
                ct
            )
        )
            .Should()
            .Be(1);
        (
            await persistence.ReleaseInProgressCronJobOccurrenceAsync(
                JobsLeaseTables.CronOccurrence(peerOccurrenceId, 1),
                ct
            )
        )
            .Should()
            .Be(0);

        var stored = (await persistence.GetAllCronJobOccurrencesAsync(predicate: null, ct)).Single(x =>
            x.Id == occurrenceId
        );
        stored.RetryCount.Should().Be(0);
        stored.Lease.State.Should().NotBe(RowLeaseState.Active);

        switch (nodeDeathPolicy)
        {
            case NodeDeathPolicy.Retry:
                stored.Status.Should().Be(JobStatus.Idle);
                break;
            case NodeDeathPolicy.MarkFailed:
                stored.Status.Should().Be(JobStatus.Failed);
                stored.ExceptionMessage.Should().Be(_StopReason);
                break;
            default:
                stored.Status.Should().Be(JobStatus.Skipped);
                stored.SkippedReason.Should().Be(_StopReason);
                stored.Disposition.Should().Be(CronOccurrenceDisposition.Accounted);
                break;
        }

        (await Fixture.ReadCronOccurrenceAsync(peerOccurrenceId, ct))
            .Should()
            .Be(((int)JobStatus.InProgress, _StopPeer));
    }

    public virtual async Task graceful_release_returns_a_running_cron_occurrence_to_idle_uncharged()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);

        using var host = Fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        var cronId = Guid.NewGuid();
        var occurrenceId = Guid.NewGuid();
        await Fixture.SeedCronJobAsync(cronId, "GracefulCronRelease", "* * * * *", NodeDeathPolicy.Skip, ct);
        await Fixture.SeedCronOccurrenceAsync(
            occurrenceId,
            cronId,
            (int)JobStatus.InProgress,
            _StopOwner,
            NodeDeathPolicy.Skip,
            DateTime.UtcNow.AddMinutes(5),
            DateTime.UtcNow.AddMinutes(-1),
            ct
        );

        (await persistence.ReleaseInProgressCronJobOccurrenceAsync(JobsLeaseTables.CronOccurrence(occurrenceId, 1), ct))
            .Should()
            .Be(1);

        (await Fixture.ReadCronOccurrenceAsync(occurrenceId, ct)).Should().Be(((int)JobStatus.Idle, null));
        (await Fixture.ReadCronOccurrenceClaimAsync(occurrenceId, ct)).State.Should().NotBe(RowLeaseState.Active);
    }

    /// <summary>
    /// A zero-retry job running when its host stops gracefully is handed back with its budget intact, so the next
    /// node runs it once instead of failing it for an attempt the stop interrupted.
    /// </summary>
    public virtual async Task graceful_stop_hands_a_zero_retry_job_to_the_next_node_which_runs_it_once()
    {
        var ct = AbortToken;
        await Fixture.ResetDatabaseAsync(ct);
        var id = Guid.NewGuid();

        using (var first = _BuildGracefulStopHost("node-a"))
        {
            await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(first, ct);
            await first.StartAsync(ct);
            var store = first.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await store.AddTimeJobsAsync(
                [
                    new TimeJobEntity
                    {
                        Id = id,
                        Function = GracefulStopJobsModule.FunctionName,
                        ExecutionTime = DateTime.UtcNow,
                        Retries = 0,
                        OnNodeDeath = NodeDeathPolicy.Retry,
                    },
                ],
                ct
            );
            await GracefulStopJobsModule.FirstRunStarted(id).WaitAsync(TimeSpan.FromSeconds(60), ct);

            // A short stop budget: the running job is cancelled when only its release window remains and must still
            // hand its row back before the stop returns.
            await first.StopAsync(ct);
        }

        var (releasedStatus, releasedOwnerId) = await Fixture.ReadTimeJobAsync(id, ct);
        releasedStatus.Should().Be((int)JobStatus.Idle);
        releasedOwnerId.Should().BeNull();
        (await Fixture.ReadTimeJobRetryCountAsync(id, ct)).Should().Be(0, "a graceful stop is not a used-up attempt");

        using var second = _BuildGracefulStopHost("node-b");
        await second.StartAsync(ct);
        try
        {
            var store = second.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            TimeJobEntity? done = null;
            while (DateTime.UtcNow < deadline)
            {
                done = await store.GetTimeJobByIdAsync(id, ct);
                if (done?.Status is JobStatus.Succeeded or JobStatus.DueDone or JobStatus.Failed)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }

            done!.Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.DueDone);
            done.RetryCount.Should().Be(0);
            GracefulStopJobsModule
                .Invocations(id)
                .Should()
                .Be(2, "the first node was interrupted once, the next ran it once");
        }
        finally
        {
            await second.StopAsync(ct);
        }
    }

    // Read on its own connection, so the bound around a store-stamped instant absorbs only round-trip latency.
    private async Task<DateTime> _StoreUtcNowAsync(CancellationToken cancellationToken)
    {
        await using var connection = Fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Fixture.UtcNowSqlExpression}";

        return await command.ExecuteScalarAsync(cancellationToken) switch
        {
            DateTimeOffset instant => instant.UtcDateTime,
            DateTime instant => DateTime.SpecifyKind(instant, DateTimeKind.Utc),
            _ => throw new InvalidOperationException("The store did not return an instant."),
        };
    }

    private IHost _BuildGracefulStopHost(string nodeId) =>
        Fixture.BuildHost(
            nodeId,
            configureJobs: jobs =>
                jobs.AddModule<GracefulStopJobsModule>()
                    .ConfigureScheduler(scheduler => scheduler.ShutdownTimeout = TimeSpan.FromSeconds(2)),
            runBackgroundServices: true
        );
}

/// <summary>
/// One time-job function for the graceful-stop scenario: its first run for a job waits until cancelled, every later
/// run succeeds. State is keyed by job id, so parallel scenarios never share it.
/// </summary>
public sealed class GracefulStopJobsModule : IJobsModule
{
    public const string FunctionName = "Graceful_Stop_Sample";

    private static readonly ConcurrentDictionary<Guid, int> _Invocations = new();
    private static readonly ConcurrentDictionary<Guid, TaskCompletionSource> _FirstRunStarted = new();

    private GracefulStopJobsModule() { }

    public static Task FirstRunStarted(Guid jobId) => _Started(jobId).Task;

    public static int Invocations(Guid jobId) => _Invocations.GetValueOrDefault(jobId);

    private static TaskCompletionSource _Started(Guid jobId) =>
        _FirstRunStarted.GetOrAdd(jobId, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    private static async Task _RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (_Invocations.AddOrUpdate(jobId, 1, static (_, count) => count + 1) > 1)
        {
            return;
        }

        _Started(jobId).TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [FunctionName] = new JobFunctionRegistration
                {
                    CronExpression = string.Empty,
                    Priority = JobPriority.Normal,
                    Delegate = static (_, context, cancellationToken) => _RunAsync(context.Id, cancellationToken),
                    MaxConcurrency = 0,
                    JobType = typeof(GracefulStopJob),
                },
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [FunctionName] = new(FunctionName, null, string.Empty, JobPriority.Normal, 0),
            }
        );
    }

    public sealed class GracefulStopJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
