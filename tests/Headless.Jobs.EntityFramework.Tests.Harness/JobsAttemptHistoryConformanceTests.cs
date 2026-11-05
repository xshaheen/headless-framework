// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Attempt history on a relational store: an attempt opens per handler invocation, every write that moves a run out of
/// InProgress closes it atomically with its outcome (inserting it closed when the open was lost), only the write that
/// wins a run closes its attempt, and deleting a run, a chain, or a cron definition deletes the attempts with it.
/// </summary>
public abstract class JobsAttemptHistoryConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Function = "attempts-job";
    private const string _Fingerprint = "v1:0123456789abcdef";
    private static readonly DateTime _StartedAt = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _EndedAt = new(2026, 11, 5, 12, 0, 5, DateTimeKind.Utc);

    public virtual async Task retry_writes_and_the_success_write_close_three_attempts_in_order()
    {
        await _RunAsync(
            "attempts-retries",
            async (store, owner, ct) =>
            {
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, owner, lapsed: false, NodeDeathPolicy.Retry, ct);

                for (var retry = 1; retry <= 2; retry++)
                {
                    (await store.OpenTimeJobAttemptAsync(job, owner, _StartedAt, ct)).Should().BeTrue();
                    var retryWrite = _State(job, owner)
                        .SetProperty(x => x.RetryCount, retry)
                        .SetProperty(x => x.FailureFingerprint, _Fingerprint)
                        .SetProperty(x => x.AttemptEnd, _End(JobAttemptOutcome.Failed, $"boom {retry}", _Fingerprint));
                    (await store.UpdateTimeJobAsync(retryWrite, ct)).Should().Be(1);
                }

                (await store.OpenTimeJobAttemptAsync(job, owner, _StartedAt, ct)).Should().BeTrue();
                var success = _State(job, owner)
                    .SetProperty(x => x.Status, JobStatus.Succeeded)
                    .SetProperty(x => x.AttemptEnd, _End(JobAttemptOutcome.Succeeded));
                (await store.UpdateTimeJobAsync(success, ct)).Should().Be(1);

                var attempts = await store.GetTimeJobAttemptsAsync(job, ct);
                attempts.Select(x => x.Number).Should().Equal(1, 2, 3);
                attempts
                    .Select(x => x.Outcome)
                    .Should()
                    .Equal(JobAttemptOutcome.Failed, JobAttemptOutcome.Failed, JobAttemptOutcome.Succeeded);
                attempts
                    .Should()
                    .AllSatisfy(attempt =>
                    {
                        attempt.RunId.Should().Be(job);
                        attempt.OwnerId.Should().Be(owner);
                        attempt.StartedAt.Should().Be(_StartedAt);
                        attempt.EndedAt.Should().Be(_EndedAt);
                    });
                attempts[0].ExceptionMessage.Should().Be("boom 1");
                attempts[1].ExceptionMessage.Should().Be("boom 2");
                attempts[0].FailureFingerprint.Should().Be(_Fingerprint);
                attempts[2].ExceptionMessage.Should().BeNull();
                attempts[2].FailureFingerprint.Should().BeNull();
            }
        );
    }

    public virtual async Task store_failing_a_run_of_a_dead_node_leaves_no_open_attempt()
    {
        await _RunAsync(
            "attempts-dead-failed",
            async (store, _, ct) =>
            {
                const string dead = "attempts-dead@1";
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, dead, lapsed: true, NodeDeathPolicy.MarkFailed, ct);
                var occurrence = await _SeedRunningOccurrenceAsync(dead, NodeDeathPolicy.MarkFailed, lapsed: true, ct);
                (await store.OpenTimeJobAttemptAsync(job, dead, _StartedAt, ct)).Should().BeTrue();
                (await store.OpenCronJobOccurrenceAttemptAsync(occurrence, dead, _StartedAt, ct)).Should().BeTrue();

                (await store.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct)).Should().Be(1);
                (await store.ReleaseDeadNodeOccurrenceResourcesAsync(dead, ct)).Should().Be(1);

                var attempt = (await store.GetTimeJobAttemptsAsync(job, ct)).Should().ContainSingle().Subject;
                attempt.EndedAt.Should().NotBeNull();
                attempt.StartedAt.Should().Be(_StartedAt);
                attempt.OwnerId.Should().Be(dead);
                attempt.Outcome.Should().Be(JobAttemptOutcome.Failed);
                attempt.FailureFingerprint.Should().Be(JobFailureFingerprints.NodeNotAlive);
                attempt.ExceptionMessage.Should().Be("Node is not alive!");

                var occurrenceAttempt = (await store.GetCronJobOccurrenceAttemptsAsync(occurrence, ct))
                    .Should()
                    .ContainSingle()
                    .Subject;
                occurrenceAttempt.EndedAt.Should().NotBeNull();
                occurrenceAttempt.Outcome.Should().Be(JobAttemptOutcome.Failed);
                occurrenceAttempt.FailureFingerprint.Should().Be(JobFailureFingerprints.NodeNotAlive);
            }
        );
    }

    public virtual async Task node_death_reclaim_to_idle_closes_the_attempt_as_lost_and_the_next_claim_opens_another()
    {
        await _RunAsync(
            "attempts-dead-idle",
            async (store, owner, ct) =>
            {
                const string dead = "attempts-dead-idle@1";
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, dead, lapsed: true, NodeDeathPolicy.Retry, ct);
                (await store.OpenTimeJobAttemptAsync(job, dead, _StartedAt, ct)).Should().BeTrue();

                (await store.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct)).Should().Be(1);
                (await store.AcquireImmediateTimeJobsAsync([job], ct)).Should().ContainSingle();
                (await store.OpenTimeJobAttemptAsync(job, owner, _StartedAt, ct)).Should().BeTrue();

                var attempts = await store.GetTimeJobAttemptsAsync(job, ct);
                attempts.Select(x => x.Number).Should().Equal(1, 2);
                attempts[0].Outcome.Should().Be(JobAttemptOutcome.Lost);
                attempts[0].OwnerId.Should().Be(dead);
                attempts[0].EndedAt.Should().NotBeNull();
                attempts[1].OwnerId.Should().Be(owner);
                attempts[1].Outcome.Should().BeNull("the attempt of the new claim is still running");
                attempts[1].EndedAt.Should().BeNull();
            }
        );
    }

    public virtual async Task skip_reclaim_stalled_lease_and_shutdown_release_each_close_the_attempt_with_their_outcome()
    {
        await _RunAsync(
            "attempts-outcomes",
            async (store, owner, ct) =>
            {
                const string dead = "attempts-skip@1";
                var skipped = Guid.NewGuid();
                var stalled = Guid.NewGuid();
                var released = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(skipped, dead, lapsed: true, NodeDeathPolicy.Skip, ct);
                await _SeedRunningTimeJobAsync(stalled, owner, lapsed: true, NodeDeathPolicy.Retry, ct);
                await _SeedRunningTimeJobAsync(released, owner, lapsed: false, NodeDeathPolicy.Retry, ct);
                var stalledOccurrence = await _SeedRunningOccurrenceAsync(
                    owner,
                    NodeDeathPolicy.Retry,
                    lapsed: true,
                    ct
                );
                (await store.OpenTimeJobAttemptAsync(skipped, dead, _StartedAt, ct)).Should().BeTrue();
                (await store.OpenTimeJobAttemptAsync(stalled, owner, _StartedAt, ct)).Should().BeTrue();
                (await store.OpenTimeJobAttemptAsync(released, owner, _StartedAt, ct)).Should().BeTrue();
                (await store.OpenCronJobOccurrenceAttemptAsync(stalledOccurrence, owner, _StartedAt, ct))
                    .Should()
                    .BeTrue();

                (await store.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct)).Should().Be(1);
                (await store.ReclaimStalledTimeJobsAsync(ct)).Should().Be(1);
                (await store.ReclaimStalledCronJobOccurrencesAsync(ct)).Should().Be(1);
                (await store.SettleInterruptedTimeJobAsync(released, owner, "host stopped", null, ct)).Should().Be(1);

                var skippedAttempt = await _OnlyClosedAttemptAsync(store, skipped, ct);
                skippedAttempt.Outcome.Should().Be(JobAttemptOutcome.Skipped);
                skippedAttempt.ExceptionMessage.Should().Be("Node is not alive!");
                var lost = await _OnlyClosedAttemptAsync(store, stalled, ct);
                lost.Outcome.Should().Be(JobAttemptOutcome.Lost);
                lost.ExceptionMessage.Should().Be("Lease lapsed while running!");
                var handedBack = await _OnlyClosedAttemptAsync(store, released, ct);
                handedBack.Outcome.Should().Be(JobAttemptOutcome.Released);
                handedBack.ExceptionMessage.Should().Be("host stopped");
                var lostOccurrence = (await store.GetCronJobOccurrenceAttemptsAsync(stalledOccurrence, ct))
                    .Should()
                    .ContainSingle()
                    .Subject;
                lostOccurrence.Outcome.Should().Be(JobAttemptOutcome.Lost);
            }
        );
    }

    public virtual async Task a_close_with_no_open_attempt_inserts_one_closed_attempt_with_what_it_knows()
    {
        await _RunAsync(
            "attempts-lost-open",
            async (store, owner, ct) =>
            {
                const string dead = "attempts-lost-open@1";
                var completed = Guid.NewGuid();
                var swept = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(completed, owner, lapsed: false, NodeDeathPolicy.Retry, ct);
                await _SeedRunningTimeJobAsync(swept, dead, lapsed: true, NodeDeathPolicy.MarkFailed, ct);

                var failure = _State(completed, owner)
                    .SetProperty(x => x.Status, JobStatus.Failed)
                    .SetProperty(x => x.AttemptEnd, _End(JobAttemptOutcome.Failed, "boom", _Fingerprint));
                (await store.UpdateTimeJobAsync(failure, ct)).Should().Be(1);
                (await store.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct)).Should().Be(1);

                var executorClosed = await _OnlyClosedAttemptAsync(store, completed, ct);
                executorClosed.Number.Should().Be(1);
                executorClosed.OwnerId.Should().Be(owner);
                executorClosed.StartedAt.Should().Be(_StartedAt);
                executorClosed.EndedAt.Should().Be(_EndedAt);
                executorClosed.Outcome.Should().Be(JobAttemptOutcome.Failed);
                executorClosed.ExceptionMessage.Should().Be("boom");
                executorClosed.FailureFingerprint.Should().Be(_Fingerprint);

                var storeClosed = await _OnlyClosedAttemptAsync(store, swept, ct);
                storeClosed.Number.Should().Be(1);
                storeClosed.OwnerId.Should().Be(dead);
                storeClosed.StartedAt.Should().BeNull("the store does not know when the lost attempt started");
                storeClosed.Outcome.Should().Be(JobAttemptOutcome.Failed);
                storeClosed.FailureFingerprint.Should().Be(JobFailureFingerprints.NodeNotAlive);
            }
        );
    }

    public virtual async Task a_graceful_release_of_a_run_that_never_started_inserts_no_attempt()
    {
        await _RunAsync(
            "attempts-release",
            async (store, owner, ct) =>
            {
                var job = Guid.NewGuid();
                await _SeedRunningTimeJobAsync(job, owner, lapsed: false, NodeDeathPolicy.Retry, ct);

                (await store.ReleaseInProgressTimeJobAsync(job, owner, ct)).Should().Be(1);
                (await store.OpenTimeJobAttemptAsync(job, owner, _StartedAt, ct))
                    .Should()
                    .BeFalse("a run that left InProgress takes no new attempt");

                (await store.GetTimeJobAttemptsAsync(job, ct)).Should().BeEmpty();
            }
        );
    }

    public virtual async Task deleting_a_chain_deletes_every_members_attempts()
    {
        await _RunAsync(
            "attempts-chain-delete",
            async (store, _, ct) =>
            {
                var root = Guid.NewGuid();
                var child = Guid.NewGuid();
                var unrelated = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(root, _Function, (int)JobStatus.Failed, null, ct);
                await fixture.SeedTimeJobAsync(child, _Function, (int)JobStatus.Idle, null, ct);
                await fixture.SeedTimeJobAsync(unrelated, _Function, (int)JobStatus.Failed, null, ct);
                await _SetParentAsync(child, root, ct);
                await _AddClosedAttemptsAsync(ct, root, child, unrelated);

                (await store.RemoveTimeJobsAsync([root], ct)).Should().Be(2);

                (await _CountTimeJobAttemptsAsync(ct)).Should().Be(1, "only the unrelated run keeps its attempt");
                (await store.GetTimeJobAttemptsAsync(unrelated, ct)).Should().ContainSingle();
            }
        );
    }

    public virtual async Task deleting_a_cron_job_deletes_its_occurrences_attempts()
    {
        await _RunAsync(
            "attempts-cron-delete",
            async (store, owner, ct) =>
            {
                var occurrence = await _SeedRunningOccurrenceAsync(owner, NodeDeathPolicy.Retry, lapsed: false, ct);
                (await store.OpenCronJobOccurrenceAttemptAsync(occurrence, owner, _StartedAt, ct)).Should().BeTrue();
                var cronJobId = (await store.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence, ct))
                    .Single()
                    .CronJobId;

                (await store.RemoveCronJobsAsync([cronJobId], ct)).Should().Be(1);

                (await store.GetCronJobOccurrenceAttemptsAsync(occurrence, ct)).Should().BeEmpty();
            }
        );
    }

    public virtual async Task an_attempt_closes_once_when_two_writers_race_to_end_a_run()
    {
        await _RunAsync(
            "attempts-race",
            async (store, owner, ct) =>
            {
                // Repeated so both orders of the race are exercised; each round asserts whichever order happened.
                for (var round = 0; round < 10; round++)
                {
                    var job = Guid.NewGuid();
                    await _SeedRunningTimeJobAsync(job, owner, lapsed: true, NodeDeathPolicy.MarkFailed, ct);
                    (await store.OpenTimeJobAttemptAsync(job, owner, _StartedAt, ct)).Should().BeTrue();
                    var success = _State(job, owner)
                        .SetProperty(x => x.Status, JobStatus.Succeeded)
                        .SetProperty(x => x.AttemptEnd, _End(JobAttemptOutcome.Succeeded));

                    var executor = Task.Run(() => store.UpdateTimeJobAsync(success, ct), ct);
                    var sweep = Task.Run(() => store.ReclaimStalledTimeJobsAsync(ct), ct);
                    var secondSweep = Task.Run(() => store.ReclaimStalledTimeJobsAsync(ct), ct);
                    var executorWrote = await executor;
                    var sweepsWrote = await sweep + await secondSweep;

                    (executorWrote + sweepsWrote).Should().Be(1, "exactly one writer ends the run");
                    var attempt = await _OnlyClosedAttemptAsync(store, job, ct);
                    attempt
                        .Outcome.Should()
                        .Be(executorWrote == 1 ? JobAttemptOutcome.Succeeded : JobAttemptOutcome.Failed);
                }
            }
        );
    }

    #region Helpers

    private async Task _RunAsync(
        string nodeId,
        Func<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>, string, CancellationToken, Task> scenario
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(nodeId);
        _host = host;
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            await scenario(_Store(host), _Owner(host), ct);
        }
        finally
        {
            await host.StopAsync(ct);
            _host = null;
        }
    }

    private IHost? _host;

    private async Task _AddClosedAttemptsAsync(CancellationToken cancellationToken, params Guid[] runIds)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        foreach (var runId in runIds)
        {
            db.Set<TimeJobAttemptEntity>()
                .Add(
                    new TimeJobAttemptEntity
                    {
                        RunId = runId,
                        Number = 1,
                        OwnerId = "attempts-seed@1",
                        StartedAt = _StartedAt,
                        EndedAt = _EndedAt,
                        Outcome = JobAttemptOutcome.Failed,
                    }
                );
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> _CountTimeJobAttemptsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();

        return await db.Set<TimeJobAttemptEntity>().CountAsync(cancellationToken);
    }

    private async Task _SetParentAsync(Guid child, Guid parent, CancellationToken cancellationToken)
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"ParentId\" = @parent WHERE \"Id\" = @child;"
        );
        JobsCoordinationFixtureExtensions.AddParameter(command, "@parent", parent);
        JobsCoordinationFixtureExtensions.AddParameter(command, "@child", child);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<TimeJobAttemptEntity> _OnlyClosedAttemptAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var attempt = (await store.GetTimeJobAttemptsAsync(id, cancellationToken)).Should().ContainSingle().Subject;
        attempt.EndedAt.Should().NotBeNull("the write that left InProgress closed the attempt");

        return attempt;
    }

    private Task _SeedRunningTimeJobAsync(
        Guid id,
        string owner,
        bool lapsed,
        NodeDeathPolicy policy,
        CancellationToken cancellationToken
    )
    {
        // Wide margins, so database-clock drift between seed and sweep cannot flip a row between lapsed and live.
        return fixture.SeedTimeJobAsync(
            id,
            _Function,
            (int)JobStatus.InProgress,
            owner,
            cancellationToken,
            policy,
            lapsed ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(10)
        );
    }

    private async Task<Guid> _SeedRunningOccurrenceAsync(
        string owner,
        NodeDeathPolicy policy,
        bool lapsed,
        CancellationToken cancellationToken
    )
    {
        var cronId = Guid.NewGuid();
        var occurrence = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, _Function, "* * * * *", policy, cancellationToken);
        await fixture.SeedCronOccurrenceAsync(
            occurrence,
            cronId,
            (int)JobStatus.InProgress,
            owner,
            policy,
            lapsed ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(10),
            DateTime.UtcNow.AddMinutes(-1),
            cancellationToken
        );

        return occurrence;
    }

    private static JobAttemptEnd _End(
        JobAttemptOutcome outcome,
        string? exceptionMessage = null,
        string? failureFingerprint = null
    )
    {
        return new JobAttemptEnd
        {
            Outcome = outcome,
            StartedAt = _StartedAt,
            EndedAt = _EndedAt,
            ExceptionMessage = exceptionMessage,
            FailureFingerprint = failureFingerprint,
        };
    }

    private static JobExecutionState _State(Guid id, string owner)
    {
        return new JobExecutionState
        {
            FunctionName = _Function,
            JobId = id,
            ClaimOwner = owner,
        };
    }

    private static string _Owner(IHost host) => host.Services.GetRequiredService<INodeMembership>().GetOwnerTag()!;

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    #endregion
}
