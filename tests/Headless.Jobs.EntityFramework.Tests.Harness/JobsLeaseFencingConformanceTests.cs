// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Fencing;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Every write that takes a row from its attempt, a reclaim, a cancel, a delete, an edit, or a sweep, ends the
/// attempt's lease in the same statement. Each scenario stages an attempt at a known generation, takes the row from it,
/// then plays the attempt's late completion: the store must refuse it and keep what the taking write stored.
/// </summary>
/// <remarks>
/// The completion carries the attempt's own owner, so these cases fail on a store that fences on the owner alone. That
/// is the gap a same-owner reclaim exposes: the node that claims its own expired row again shares the earlier
/// attempt's owner, and only the generation tells the two apart.
/// </remarks>
public abstract class JobsLeaseFencingConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _DeadOwner = "lease-fencing-dead@1";

    /// <summary>
    /// A node reclaims its own row after the lease lapsed. The reclaim takes a new generation, so the earlier attempt's
    /// completion is refused even though it names the same owner, and the new attempt's completion is accepted.
    /// </summary>
    public virtual async Task same_owner_reclaim_of_an_expired_row_refuses_the_earlier_attempts_completion()
    {
        foreach (var useNativeClaims in (bool[])[true, false])
        {
            await _AssertSameOwnerReclaimFencesTheEarlierAttemptAsync(useNativeClaims, AbortToken);
        }
    }

    /// <summary>Cancelling an idle row its claim still holds revokes the lease with the cancel.</summary>
    public virtual async Task cancel_of_a_claimed_idle_row_refuses_the_attempts_completion()
    {
        var ct = AbortToken;
        await _RunAsync(
            "lease-fencing-cancel",
            async (persistence, owner) =>
            {
                var id = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    id,
                    "fencing-cancel",
                    (int)JobStatus.Idle,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(5)
                );

                (await persistence.RequestTimeJobCancellationAsync(id, ct)).Should().BeTrue();

                var (cancelledStatus, cancelledLease, _, _) = await fixture.ReadTimeJobDetailAsync(id, ct);
                cancelledStatus.Should().Be((int)JobStatus.Cancelled);
                cancelledLease.State.Should().Be(RowLeaseState.Revoked);

                (await persistence.UpdateTimeJobAsync(_Completion(id, owner, generation: 1), ct)).Should().Be(0);
                (await fixture.ReadTimeJobDetailAsync(id, ct)).Status.Should().Be((int)JobStatus.Cancelled);
            },
            ct
        );
    }

    /// <summary>Deleting a running row leaves its attempt nothing to complete.</summary>
    public virtual async Task delete_of_a_running_row_refuses_the_attempts_completion()
    {
        var ct = AbortToken;
        await _RunAsync(
            "lease-fencing-delete",
            async (persistence, owner) =>
            {
                var id = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    id,
                    "fencing-delete",
                    (int)JobStatus.InProgress,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(5)
                );

                (await persistence.RemoveTimeJobsAsync([id], ct)).Should().Be(1);

                (await persistence.UpdateTimeJobAsync(_Completion(id, owner, generation: 1), ct)).Should().Be(0);
                (await persistence.GetTimeJobByIdAsync(id, ct)).Should().BeNull();
            },
            ct
        );
    }

    /// <summary>
    /// Editing a claimed but not started row puts it back to idle and revokes the claim, so the claimed attempt cannot
    /// complete the edited row.
    /// </summary>
    public virtual async Task edit_of_a_claimed_queued_row_refuses_the_attempts_completion()
    {
        var ct = AbortToken;
        await _RunAsync(
            "lease-fencing-edit",
            async (persistence, owner) =>
            {
                var id = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    id,
                    "fencing-edit",
                    (int)JobStatus.Queued,
                    owner,
                    ct,
                    lockedUntil: DateTime.UtcNow.AddMinutes(5)
                );

                (
                    await persistence.UpdateTimeJobsAsync(
                        [
                            new TimeJobEntity
                            {
                                Id = id,
                                Function = "fencing-edit",
                                Description = "Edited while claimed",
                            },
                        ],
                        ct
                    )
                ).Should().Be(1);

                var (editedStatus, editedLease, _, _) = await fixture.ReadTimeJobDetailAsync(id, ct);
                editedStatus.Should().Be((int)JobStatus.Idle);
                editedLease.State.Should().Be(RowLeaseState.Revoked);

                (await persistence.UpdateTimeJobAsync(_Completion(id, owner, generation: 1), ct)).Should().Be(0);
                (await fixture.ReadTimeJobDetailAsync(id, ct)).Status.Should().Be((int)JobStatus.Idle);
            },
            ct
        );
    }

    /// <summary>
    /// The dead-node sweep ends the dead attempt's lease with each policy's transition, so a falsely dead node that is
    /// still running cannot overwrite the released or failed row.
    /// </summary>
    public virtual async Task dead_node_sweep_refuses_the_swept_attempts_time_job_completion()
    {
        var ct = AbortToken;
        await _RunAsync(
            "lease-fencing-dead-node",
            async (persistence, _) =>
            {
                var retried = Guid.NewGuid();
                var failed = Guid.NewGuid();
                // No deadline seeds a lapsed lease, which the sweep requires before it takes an InProgress row.
                await fixture.SeedTimeJobAsync(
                    retried,
                    "fencing-dead-retry",
                    (int)JobStatus.InProgress,
                    _DeadOwner,
                    ct
                );
                await fixture.SeedTimeJobAsync(
                    failed,
                    "fencing-dead-fail",
                    (int)JobStatus.InProgress,
                    _DeadOwner,
                    ct,
                    onNodeDeath: NodeDeathPolicy.MarkFailed
                );

                (await persistence.ReleaseDeadNodeTimeJobResourcesAsync(_DeadOwner, ct)).Should().Be(2);

                await _AssertTimeJobCompletionRefusedAsync(persistence, retried, JobStatus.Idle, ct);
                await _AssertTimeJobCompletionRefusedAsync(persistence, failed, JobStatus.Failed, ct);
            },
            ct
        );
    }

    /// <summary>The stalled reclaim ends the lapsed attempt's lease with each policy's transition.</summary>
    public virtual async Task stalled_reclaim_refuses_the_lapsed_attempts_time_job_completion()
    {
        var ct = AbortToken;
        await _RunAsync(
            "lease-fencing-stalled",
            async (persistence, _) =>
            {
                var retried = Guid.NewGuid();
                var failed = Guid.NewGuid();
                await fixture.SeedTimeJobAsync(
                    retried,
                    "fencing-stalled-retry",
                    (int)JobStatus.InProgress,
                    _DeadOwner,
                    ct
                );
                await fixture.SeedTimeJobAsync(
                    failed,
                    "fencing-stalled-fail",
                    (int)JobStatus.InProgress,
                    _DeadOwner,
                    ct,
                    onNodeDeath: NodeDeathPolicy.MarkFailed
                );

                (await persistence.ReclaimStalledTimeJobsAsync(ct)).Should().Be(2);

                await _AssertTimeJobCompletionRefusedAsync(persistence, retried, JobStatus.Idle, ct);
                await _AssertTimeJobCompletionRefusedAsync(persistence, failed, JobStatus.Failed, ct);
            },
            ct
        );
    }

    /// <summary>The dead-node sweep of cron occurrences fences the swept attempt the same way.</summary>
    public virtual async Task dead_node_sweep_refuses_the_swept_attempts_occurrence_completion()
    {
        var ct = AbortToken;
        await _RunAsync(
            "lease-fencing-dead-node-cron",
            async (persistence, _) =>
            {
                var (retried, failed) = await _SeedLapsedOccurrencesAsync("fencing-dead-cron", ct);

                (await persistence.ReleaseDeadNodeOccurrenceResourcesAsync(_DeadOwner, ct)).Should().Be(2);

                await _AssertOccurrenceCompletionRefusedAsync(persistence, retried, JobStatus.Idle, ct);
                await _AssertOccurrenceCompletionRefusedAsync(persistence, failed, JobStatus.Failed, ct);
            },
            ct
        );
    }

    /// <summary>The stalled reclaim of cron occurrences fences the lapsed attempt the same way.</summary>
    public virtual async Task stalled_reclaim_refuses_the_lapsed_attempts_occurrence_completion()
    {
        var ct = AbortToken;
        await _RunAsync(
            "lease-fencing-stalled-cron",
            async (persistence, _) =>
            {
                var (retried, failed) = await _SeedLapsedOccurrencesAsync("fencing-stalled-cron", ct);

                (await persistence.ReclaimStalledCronJobOccurrencesAsync(ct)).Should().Be(2);

                await _AssertOccurrenceCompletionRefusedAsync(persistence, retried, JobStatus.Idle, ct);
                await _AssertOccurrenceCompletionRefusedAsync(persistence, failed, JobStatus.Failed, ct);
            },
            ct
        );
    }

    private async Task _AssertSameOwnerReclaimFencesTheEarlierAttemptAsync(bool useNativeClaims, CancellationToken ct)
    {
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            useNativeClaims ? "lease-fencing-reclaim-native" : "lease-fencing-reclaim-generic",
            useNativeClaims: useNativeClaims
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var path = useNativeClaims ? "native" : "generic";
            var id = Guid.NewGuid();
            // Overdue, so the fallback sweep claims it; that sweep is the claim that takes back a queued row whose
            // lease lapsed, and it runs on the node that queued it as on any other.
            await fixture.SeedTimeJobAsync(
                id,
                "fencing-reclaim",
                (int)JobStatus.Idle,
                ownerId: null,
                ct,
                executionTime: DateTime.UtcNow.AddMinutes(-1)
            );

            var first = (await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct))
                .Should()
                .ContainSingle($"the {path} claim takes the overdue row")
                .Subject;

            // The first dispatch stalled past its lease before it started the row, and nothing swept it.
            await fixture.OverwriteLeaseAsync(
                fixture.QualifiedTimeJobsTable,
                id,
                first.Lease with
                {
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                },
                ct
            );

            var second = (await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct))
                .Should()
                .ContainSingle($"the {path} claim takes back its own expired row")
                .Subject;

            second.Lease.Holder.Should().Be(first.Lease.Holder, "the same node reclaimed the row");
            second.Lease.Generation.Should().BeGreaterThan(first.Lease.Generation);

            var (reclaimedStatus, reclaimedLease, _, _) = await fixture.ReadTimeJobDetailAsync(id, ct);

            (await persistence.UpdateTimeJobAsync(_Completion(id, first.Lease.Holder!, first.Lease.Generation), ct))
                .Should()
                .Be(0, $"the {path} reclaim fenced off the earlier attempt");

            var (afterLateCompletionStatus, afterLateCompletionLease, _, _) = await fixture.ReadTimeJobDetailAsync(
                id,
                ct
            );
            afterLateCompletionStatus.Should().Be(reclaimedStatus);
            afterLateCompletionLease.Should().Be(reclaimedLease);

            (await persistence.UpdateTimeJobAsync(_Completion(id, second.Lease.Holder!, second.Lease.Generation), ct))
                .Should()
                .Be(1, "the current attempt still completes");
            (await fixture.ReadTimeJobDetailAsync(id, ct)).Status.Should().Be((int)JobStatus.Succeeded);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task _RunAsync(
        string nodeId,
        Func<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>, string, Task> scenario,
        CancellationToken ct
    )
    {
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(nodeId);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().GetOwnerTag()!;
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            await scenario(persistence, owner);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task<(Guid Retried, Guid Failed)> _SeedLapsedOccurrencesAsync(string name, CancellationToken ct)
    {
        var retryCron = Guid.NewGuid();
        var failCron = Guid.NewGuid();
        await fixture.SeedCronJobAsync(retryCron, name + "-retry", "* * * * *", NodeDeathPolicy.Retry, ct);
        await fixture.SeedCronJobAsync(failCron, name + "-fail", "* * * * *", NodeDeathPolicy.MarkFailed, ct);

        var retried = Guid.NewGuid();
        var failed = Guid.NewGuid();
        var executionTime = DateTime.UtcNow.AddMinutes(-1);
        await fixture.SeedCronOccurrenceAsync(
            retried,
            retryCron,
            (int)JobStatus.InProgress,
            _DeadOwner,
            NodeDeathPolicy.Retry,
            lockedUntil: null,
            executionTime,
            ct
        );
        await fixture.SeedCronOccurrenceAsync(
            failed,
            failCron,
            (int)JobStatus.InProgress,
            _DeadOwner,
            NodeDeathPolicy.MarkFailed,
            lockedUntil: null,
            executionTime,
            ct
        );

        return (retried, failed);
    }

    private async Task _AssertTimeJobCompletionRefusedAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid id,
        JobStatus swept,
        CancellationToken ct
    )
    {
        var (afterSweepStatus, afterSweepLease, _, _) = await fixture.ReadTimeJobDetailAsync(id, ct);
        afterSweepStatus.Should().Be((int)swept);
        afterSweepLease.State.Should().NotBe(RowLeaseState.Active, "the sweep ends the attempt's lease");

        (await persistence.UpdateTimeJobAsync(_Completion(id, _DeadOwner, generation: 1), ct)).Should().Be(0);
        (await fixture.ReadTimeJobDetailAsync(id, ct)).Status.Should().Be((int)swept);
    }

    private async Task _AssertOccurrenceCompletionRefusedAsync(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence,
        Guid id,
        JobStatus swept,
        CancellationToken ct
    )
    {
        (await fixture.ReadCronOccurrenceAsync(id, ct)).Should().Be(((int)swept, (string?)null));

        var completion = new JobExecutionState
        {
            FunctionName = "fencing-cron",
            JobId = id,
            Type = JobType.CronJobOccurrence,
            ClaimOwner = _DeadOwner,
            ClaimGeneration = 1,
        }.SetProperty(x => x.Status, JobStatus.Succeeded);

        (await persistence.UpdateCronJobOccurrenceAsync(completion, ct)).Should().Be(0);
        (await fixture.ReadCronOccurrenceAsync(id, ct)).Status.Should().Be((int)swept);
    }

    private static JobExecutionState _Completion(Guid id, string owner, long generation)
    {
        return new JobExecutionState
        {
            FunctionName = "fencing",
            JobId = id,
            ClaimOwner = owner,
            ClaimGeneration = generation,
        }.SetProperty(x => x.Status, JobStatus.Succeeded);
    }
}
