// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Cross-provider Jobs+Coordination conformance scenarios that must hold identically on every backend:
/// <list type="bullet">
/// <item>R1 — a durable node stamps work with its <c>node@incarnation</c> coordination owner.</item>
/// <item>R4 — the strict reclaim predicate touches only the dead incarnation's non-terminal rows.</item>
/// <item>reclaim is idempotent (a second pass affects zero rows).</item>
/// <item>R2/R3 — a surviving node recovers a crashed node's work end-to-end via the coordination
/// <c>NodeLeft</c> event, with no Redis in the wiring.</item>
/// </list>
/// Each leaf derives a sealed class with <c>[Collection&lt;TFixture&gt;]</c> and re-declares the methods with
/// <c>[Fact]</c> so the runner discovers them per provider.
/// </summary>
public abstract class JobsCoordinationConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task time_job_cancellation_is_atomic_durable_and_preserves_rejected_audit_state()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("cancellation-node");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var idleId = Guid.NewGuid();
            var queuedId = Guid.NewGuid();
            var inProgressId = Guid.NewGuid();
            var terminalId = Guid.NewGuid();
            var lockedUntil = DateTime.UtcNow.AddMinutes(5);

            await fixture.SeedTimeJobAsync(
                idleId,
                "cancel-idle",
                (int)JobStatus.Idle,
                owner,
                ct,
                lockedUntil: lockedUntil
            );
            await fixture.SeedTimeJobAsync(
                queuedId,
                "cancel-queued",
                (int)JobStatus.Queued,
                owner,
                ct,
                lockedUntil: lockedUntil
            );
            await fixture.SeedTimeJobAsync(
                inProgressId,
                "cancel-running",
                (int)JobStatus.InProgress,
                owner,
                ct,
                lockedUntil: lockedUntil
            );
            await fixture.SeedTimeJobAsync(
                terminalId,
                "cancel-terminal",
                (int)JobStatus.Succeeded,
                owner,
                ct,
                lockedUntil: lockedUntil
            );
            var terminalBefore = await persistence.GetTimeJobByIdAsync(terminalId, ct);

            (await persistence.IsTimeJobCancellationRequestedAsync(inProgressId, ct)).Should().BeFalse();
            (await persistence.IsTimeJobCancellationRequestedAsync(queuedId, ct)).Should().BeNull();
            (await persistence.RequestTimeJobCancellationAsync(idleId, ct)).Should().BeTrue();
            var queuedRequests = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => persistence.RequestTimeJobCancellationAsync(queuedId, ct))
            );
            (await persistence.RequestTimeJobCancellationAsync(inProgressId, ct)).Should().BeTrue();

            queuedRequests.Count(static accepted => accepted).Should().Be(1);
            (await persistence.RequestTimeJobCancellationAsync(queuedId, ct)).Should().BeFalse();
            (await persistence.RequestTimeJobCancellationAsync(terminalId, ct)).Should().BeFalse();
            (await persistence.RequestTimeJobCancellationAsync(Guid.NewGuid(), ct)).Should().BeFalse();
            (await persistence.IsTimeJobCancellationRequestedAsync(inProgressId, ct)).Should().BeTrue();
            (await persistence.IsTimeJobCancellationRequestedAsync(terminalId, ct)).Should().BeNull();

            var idle = await persistence.GetTimeJobByIdAsync(idleId, ct);
            idle!.Status.Should().Be(JobStatus.Cancelled);
            idle.CancelRequested.Should().BeTrue();
            idle.ExecutedAt.Should().NotBeNull();
            idle.OwnerId.Should().BeNull();
            idle.LockedUntil.Should().BeNull();

            var queued = await persistence.GetTimeJobByIdAsync(queuedId, ct);
            queued!.Status.Should().Be(JobStatus.Queued);
            queued.CancelRequested.Should().BeTrue();
            queued.ExecutedAt.Should().BeNull();
            queued.OwnerId.Should().Be(owner);
            queued.LockedUntil.Should().NotBeNull();

            var inProgress = await persistence.GetTimeJobByIdAsync(inProgressId, ct);
            inProgress!.Status.Should().Be(JobStatus.InProgress);
            inProgress.CancelRequested.Should().BeTrue();
            inProgress.ExecutedAt.Should().BeNull();
            inProgress.OwnerId.Should().Be(owner);
            inProgress.LockedUntil.Should().NotBeNull();

            var terminalAfter = await persistence.GetTimeJobByIdAsync(terminalId, ct);
            terminalAfter!.Status.Should().Be(JobStatus.Succeeded);
            terminalAfter.CancelRequested.Should().BeFalse();
            terminalAfter.OwnerId.Should().Be(terminalBefore!.OwnerId);
            terminalAfter.LockedUntil.Should().Be(terminalBefore.LockedUntil);
            terminalAfter.UpdatedAt.Should().Be(terminalBefore.UpdatedAt);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task idle_parent_cancellation_applies_terminal_run_conditions_in_one_transaction()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("cancellation-chain");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var eligible = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "eligible-child",
                RunCondition = RunCondition.OnCancelled,
            };
            var rejectedGrandchild = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "rejected-grandchild",
                RunCondition = RunCondition.OnCancelled,
            };
            var rejected = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "rejected-child",
                RunCondition = RunCondition.OnSuccess,
                Children = [rejectedGrandchild],
            };
            var root = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "cancelled-root",
                ExecutionTime = DateTime.UtcNow.AddMinutes(10),
                Children = [eligible, rejected],
            };
            (await persistence.AddTimeJobsAsync([root], ct)).Should().Be(4);

            (await persistence.RequestTimeJobCancellationAsync(root.Id, ct)).Should().BeTrue();

            var cancelled = await persistence.GetTimeJobByIdAsync(root.Id, ct);
            cancelled!.Status.Should().Be(JobStatus.Cancelled);

            var released = await persistence.GetTimeJobByIdAsync(eligible.Id, ct);
            released!.Status.Should().Be(JobStatus.Idle);
            released.ExecutionTime.Should().NotBeNull();

            var skipped = await persistence.GetTimeJobByIdAsync(rejected.Id, ct);
            skipped!.Status.Should().Be(JobStatus.Skipped);
            skipped.ExecutedAt.Should().NotBeNull();

            var skippedDescendant = await persistence.GetTimeJobByIdAsync(rejectedGrandchild.Id, ct);
            skippedDescendant!.Status.Should().Be(JobStatus.Skipped);
            skippedDescendant.ExecutedAt.Should().NotBeNull();
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task queued_job_is_stamped_with_the_node_incarnation_owner()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            // The gate registers during StartingAsync, which completes before StartAsync returns.
            var membership = host.Services.GetRequiredService<INodeMembership>();
            membership.Identity.Should().NotBeNull();
            var owner = membership.Identity!.Value.ToString();
            owner.Should().StartWith("node-a@");

            var jobId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(jobId, "Stamp_Sample", (int)JobStatus.Idle, ownerId: null, ct);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // Fetch the row with its current UpdatedAt (QueueTimeJobsAsync uses optimistic concurrency on it), then stamp.
            var idle = await persistence.GetTimeJobsAsync(x => x.Id == jobId, ct);
            idle.Should().ContainSingle();

            var stamped = await persistence.QueueTimeJobsAsync(idle, ct).ToListAsync(ct);
            stamped.Should().ContainSingle();

            var (status, ownerId) = await fixture.ReadTimeJobAsync(jobId, ct);
            status.Should().Be((int)JobStatus.Queued);
            ownerId.Should().Be(owner);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #469: relational claims stamp their lease from the database clock inside the claim statement. A node whose
    /// application clock is one hour slow must still persist a lease approximately one LeaseDuration ahead of DB time.
    /// </summary>
    public virtual async Task queued_job_lease_uses_the_db_clock_not_a_skewed_claimant_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-skew", timeProvider: new SkewedTimeProvider(TimeSpan.FromHours(-1)));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(id, "DbClockClaim", (int)JobStatus.Idle, ownerId: null, ct);
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var idle = await persistence.GetTimeJobsAsync(x => x.Id == id, ct);

            (await persistence.QueueTimeJobsAsync(idle, ct).ToListAsync(ct)).Should().ContainSingle();

            var lockedUntil = (await fixture.ReadTimeJobDetailAsync(id, ct)).LockedUntil;
            lockedUntil.Should().NotBeNull();
            lockedUntil!.Value.Should().BeAfter(DateTime.UtcNow.AddMinutes(4));
            lockedUntil.Value.Should().BeBefore(DateTime.UtcNow.AddMinutes(6));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// Portable EF claims must return the exact timestamps stamped by the database, even when the application clock is
    /// skewed. The mixed new/existing cron batch also pins input-order preservation after new occurrences are claimed in
    /// one database update.
    /// </summary>
    public virtual async Task portable_claim_results_return_database_timestamps_and_batch_new_crons()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost(
            "portable-skew",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromHours(-1)),
            useNativeClaims: false
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var directTimeJobId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                directTimeJobId,
                "PortableDirectTime",
                (int)JobStatus.Idle,
                ownerId: null,
                ct
            );
            var directCandidate = await persistence.GetTimeJobsAsync(x => x.Id == directTimeJobId, ct);
            var directTimeClaim = (await persistence.QueueTimeJobsAsync(directCandidate, ct).ToArrayAsync(ct))
                .Should()
                .ContainSingle()
                .Which;
            var directTimePersisted = await fixture.ReadTimeJobClaimTimestampsAsync(directTimeJobId, ct);
            directTimeClaim.LockedUntil.Should().Be(directTimePersisted.LockedUntil);
            directTimeClaim.UpdatedAt.Should().Be(directTimePersisted.UpdatedAt);

            var fallbackTimeJob = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "PortableFallbackTime",
                ExecutionTime = DateTime.UtcNow.AddHours(-2),
            };
            await persistence.AddTimeJobsAsync([fallbackTimeJob], ct);
            var fallbackTimeClaim = (await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct))
                .Should()
                .ContainSingle()
                .Which;
            var fallbackTimePersisted = await fixture.ReadTimeJobClaimTimestampsAsync(fallbackTimeJob.Id, ct);
            fallbackTimeClaim.LockedUntil.Should().Be(fallbackTimePersisted.LockedUntil);
            fallbackTimeClaim.UpdatedAt.Should().Be(fallbackTimePersisted.UpdatedAt);

            var executionTime = DateTime.UtcNow.AddMinutes(1);
            var newCronId1 = Guid.NewGuid();
            var existingCronId = Guid.NewGuid();
            var newCronId2 = Guid.NewGuid();
            await fixture.SeedCronJobAsync(newCronId1, "PortableNewCron1", "* * * * *", NodeDeathPolicy.Retry, ct);
            await fixture.SeedCronJobAsync(
                existingCronId,
                "PortableExistingCron",
                "* * * * *",
                NodeDeathPolicy.Retry,
                ct
            );
            await fixture.SeedCronJobAsync(newCronId2, "PortableNewCron2", "* * * * *", NodeDeathPolicy.Retry, ct);

            var existingOccurrenceId = Guid.NewGuid();
            var existingCreatedAt = DateTime.UtcNow.AddMinutes(-5);
            await fixture.SeedCronOccurrenceAsync(
                existingOccurrenceId,
                existingCronId,
                (int)JobStatus.Idle,
                ownerId: null,
                NodeDeathPolicy.Retry,
                lockedUntil: null,
                executionTime,
                ct
            );

            JobManagerDispatchContext[] cronContexts =
            [
                new(newCronId1) { FunctionName = "PortableNewCron1", Expression = "* * * * *" },
                new(existingCronId)
                {
                    FunctionName = "PortableExistingCron",
                    Expression = "* * * * *",
                    NextCronOccurrence = new NextCronOccurrence(existingOccurrenceId, existingCreatedAt),
                },
                new(newCronId2) { FunctionName = "PortableNewCron2", Expression = "* * * * *" },
            ];
            var directCronClaims = await persistence
                .QueueCronJobOccurrencesAsync((executionTime, cronContexts), ct)
                .ToArrayAsync(ct);

            directCronClaims.Select(x => x.CronJobId).Should().Equal(newCronId1, existingCronId, newCronId2);
            foreach (var claim in directCronClaims)
            {
                var persisted = await fixture.ReadCronOccurrenceClaimTimestampsAsync(claim.Id, ct);
                claim.LockedUntil.Should().Be(persisted.LockedUntil);
                claim.UpdatedAt.Should().Be(persisted.UpdatedAt);
            }

            var fallbackCronId = Guid.NewGuid();
            var fallbackOccurrenceId = Guid.NewGuid();
            var fallbackExecutionTime = DateTime.UtcNow.AddHours(-2);
            await fixture.SeedCronJobAsync(
                fallbackCronId,
                "PortableFallbackCron",
                "* * * * *",
                NodeDeathPolicy.Retry,
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                fallbackOccurrenceId,
                fallbackCronId,
                (int)JobStatus.Idle,
                ownerId: null,
                NodeDeathPolicy.Retry,
                lockedUntil: null,
                fallbackExecutionTime,
                ct
            );
            var fallbackCronClaim = (await persistence.QueueTimedOutCronJobOccurrencesAsync(ct).ToArrayAsync(ct))
                .Should()
                .ContainSingle()
                .Which;
            var fallbackCronPersisted = await fixture.ReadCronOccurrenceClaimTimestampsAsync(fallbackOccurrenceId, ct);
            fallbackCronClaim.LockedUntil.Should().Be(fallbackCronPersisted.LockedUntil);
            fallbackCronClaim.UpdatedAt.Should().Be(fallbackCronPersisted.UpdatedAt);

            directTimeClaim.UpdatedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
            directTimeClaim.LockedUntil.Should().BeAfter(DateTime.UtcNow.AddMinutes(4));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #469: a fast application clock must not make a still-valid foreign lease eligible in either native fallback
    /// time-job claiming or native existing-cron claiming. Both predicates are evaluated against the database clock.
    /// </summary>
    public virtual async Task native_claim_eligibility_uses_the_db_clock_not_a_fast_application_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-fast", timeProvider: new SkewedTimeProvider(TimeSpan.FromHours(1)));
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var validUntil = DateTime.UtcNow.AddMinutes(30);
            var timeJobId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                timeJobId,
                "DbClockFallbackEligibility",
                (int)JobStatus.Queued,
                "foreign@1",
                ct,
                lockedUntil: validUntil
            );

            var cronId = Guid.NewGuid();
            var occurrenceId = Guid.NewGuid();
            var executionTime = DateTime.UtcNow.AddMinutes(-1);
            await fixture.SeedCronJobAsync(cronId, "DbClockCronEligibility", "* * * * *", NodeDeathPolicy.Retry, ct);
            await fixture.SeedCronOccurrenceAsync(
                occurrenceId,
                cronId,
                (int)JobStatus.Queued,
                "foreign@2",
                NodeDeathPolicy.Retry,
                validUntil,
                executionTime,
                ct
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            (await persistence.QueueTimedOutTimeJobsAsync(ct).ToArrayAsync(ct)).Should().BeEmpty();

            var cronContext = new JobManagerDispatchContext(cronId)
            {
                FunctionName = "DbClockCronEligibility",
                Expression = "* * * * *",
                OnNodeDeath = NodeDeathPolicy.Retry,
                NextCronOccurrence = new NextCronOccurrence(occurrenceId, DateTime.UtcNow.AddMinutes(-2)),
            };
            (await persistence.QueueCronJobOccurrencesAsync((executionTime, [cronContext]), ct).ToArrayAsync(ct))
                .Should()
                .BeEmpty();

            (await fixture.ReadTimeJobAsync(timeJobId, ct)).OwnerId.Should().Be("foreign@1");
            (await fixture.ReadCronOccurrenceAsync(occurrenceId, ct)).OwnerId.Should().Be("foreign@2");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task reclaim_touches_only_the_dead_incarnations_non_terminal_rows()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";

            var reclaimedId = Guid.NewGuid(); // dead owner, Queued (Retry) -> released back to Idle
            var retryInFlightId = Guid.NewGuid(); // dead owner, InProgress + Retry -> released back to Idle (#315)
            var terminalId = Guid.NewGuid(); // dead owner, Succeeded -> untouched (terminal)
            var unownedId = Guid.NewGuid(); // no owner, Idle -> untouched
            var otherIncarnationId = Guid.NewGuid(); // node-a@6, Queued -> untouched (different incarnation)

            await fixture.SeedTimeJobAsync(reclaimedId, "Reclaimed", (int)JobStatus.Queued, dead, ct);
            // #316/U4: a dead node's InProgress row is reclaimed by the sweep only once its lease has lapsed.
            await fixture.SeedTimeJobAsync(
                retryInFlightId,
                "RetryInFlight",
                (int)JobStatus.InProgress,
                dead,
                ct,
                lockedUntil: DateTime.UtcNow.AddMinutes(-1)
            );
            await fixture.SeedTimeJobAsync(terminalId, "Terminal", (int)JobStatus.Succeeded, dead, ct);
            await fixture.SeedTimeJobAsync(unownedId, "Unowned", (int)JobStatus.Idle, ownerId: null, ct);
            await fixture.SeedTimeJobAsync(otherIncarnationId, "Other", (int)JobStatus.Queued, "node-a@6", ct);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var affected = await persistence.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct);

            // One Queued row + one InProgress-Retry row, both released back to Idle (default policy is Retry).
            affected.Should().Be(2);

            (await fixture.ReadTimeJobAsync(reclaimedId, ct)).Should().Be(((int)JobStatus.Idle, null));
            (await fixture.ReadTimeJobAsync(retryInFlightId, ct)).Should().Be(((int)JobStatus.Idle, null));
            (await fixture.ReadTimeJobAsync(terminalId, ct)).Should().Be(((int)JobStatus.Succeeded, dead));
            (await fixture.ReadTimeJobAsync(unownedId, ct)).Should().Be(((int)JobStatus.Idle, null));
            (await fixture.ReadTimeJobAsync(otherIncarnationId, ct)).Should().Be(((int)JobStatus.Queued, "node-a@6"));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task dead_node_with_mark_failed_policy_transitions_in_flight_row_to_failed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                id,
                "MarkFailed",
                (int)JobStatus.InProgress,
                dead,
                ct,
                NodeDeathPolicy.MarkFailed,
                lockedUntil: DateTime.UtcNow.AddMinutes(-1)
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var affected = await persistence.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct);

            // MarkFailed in-flight row becomes terminal Failed (never retried), owner retained for audit,
            // lease cleared (#4) and a node-death ExceptionMessage set so it's distinguishable from a run failure (#8).
            affected.Should().Be(1);
            var (status, ownerId, lockedUntil, exceptionMessage, _) = await fixture.ReadTimeJobDetailAsync(id, ct);
            status.Should().Be((int)JobStatus.Failed);
            ownerId.Should().Be(dead);
            lockedUntil.Should().BeNull();
            exceptionMessage.Should().Be("Node is not alive!");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task dead_node_with_skip_policy_transitions_in_flight_row_to_skipped()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                id,
                "Skip",
                (int)JobStatus.InProgress,
                dead,
                ct,
                NodeDeathPolicy.Skip,
                lockedUntil: DateTime.UtcNow.AddMinutes(-1)
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var affected = await persistence.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct);

            // Skip in-flight row becomes terminal Skipped (idempotency-critical: never re-run), owner retained,
            // lease cleared (#4) and SkippedReason set.
            affected.Should().Be(1);
            var (status, ownerId, lockedUntil, _, skippedReason) = await fixture.ReadTimeJobDetailAsync(id, ct);
            status.Should().Be((int)JobStatus.Skipped);
            ownerId.Should().Be(dead);
            lockedUntil.Should().BeNull();
            skippedReason.Should().Be("Node is not alive!");
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #5 completion fence: a completion write must touch only a non-terminal row still owned by the writer.
    /// Protects against a falsely-dead-but-alive node clobbering the dead-node sweep's terminal transition
    /// (owner retained on MarkFailed/Skip) or completing a row the sweep released and another node re-claimed.
    /// </summary>
    public virtual async Task completion_is_fenced_on_ownership_and_non_terminal_status()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // (a) Non-terminal row owned by a different owner — a late completion from the writer must not win.
            var foreignId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(foreignId, "foreign", (int)JobStatus.InProgress, "other-node@9", ct);

            // (b) Already-terminal row (e.g. swept to Failed) — a late completion must not resurrect/overwrite it.
            var terminalId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(terminalId, "terminal", (int)JobStatus.Failed, "node-a@5", ct);

            var foreignCompletion = new JobExecutionState { FunctionName = "foreign", JobId = foreignId }.SetProperty(
                x => x.Status,
                JobStatus.Succeeded
            );
            var terminalCompletion = new JobExecutionState
            {
                FunctionName = "terminal",
                JobId = terminalId,
            }.SetProperty(x => x.Status, JobStatus.Succeeded);

            (await persistence.UpdateTimeJobAsync(foreignCompletion, ct)).Should().Be(0);
            (await persistence.UpdateTimeJobAsync(terminalCompletion, ct)).Should().Be(0);

            // Neither row was mutated by the fenced completion.
            (await fixture.ReadTimeJobAsync(foreignId, ct))
                .Should()
                .Be(((int)JobStatus.InProgress, "other-node@9"));
            (await fixture.ReadTimeJobAsync(terminalId, ct)).Should().Be(((int)JobStatus.Failed, "node-a@5"));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #315 cron propagation: a cron job's node-death policy must flow onto every generated occurrence. The
    /// manager carries the policy on the queue context; the provider stamps and persists it on the new occurrence.
    /// </summary>
    public virtual async Task cron_occurrence_is_stamped_with_the_node_death_policy()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "cron-skip", "* * * * *", NodeDeathPolicy.Skip, ct);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // The context mirrors what the manager builds from the cron entity (OnNodeDeath copied off the cron).
            var context = new JobManagerDispatchContext(cronId)
            {
                FunctionName = "cron-skip",
                Expression = "* * * * *",
                OnNodeDeath = NodeDeathPolicy.Skip,
            };

            var occurrences = await persistence
                .QueueCronJobOccurrencesAsync((DateTime.UtcNow.AddMinutes(1), [context]), ct)
                .ToListAsync(ct);

            occurrences.Should().ContainSingle().Which.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task reclaim_is_idempotent_a_second_pass_affects_zero_rows()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";
            await fixture.SeedTimeJobAsync(Guid.NewGuid(), "A", (int)JobStatus.Queued, dead, ct);
            // #316/U4: InProgress reclaim defers to the lease — seed a lapsed lease so the row is swept this pass.
            await fixture.SeedTimeJobAsync(
                Guid.NewGuid(),
                "B",
                (int)JobStatus.InProgress,
                dead,
                ct,
                lockedUntil: DateTime.UtcNow.AddMinutes(-1)
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            (await persistence.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct)).Should().Be(2);
            (await persistence.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task surviving_node_recovers_a_crashed_nodes_work_via_node_left_event()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        // Two nodes in one cluster. No Redis anywhere in the wiring — recovery flows purely through the
        // coordination NodeLeft event into the Jobs MembershipRecoveryBridge (R3).
        var hostA = fixture.BuildHost("node-a");
        var hostB = fixture.BuildHost("node-b");

        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(hostA, ct);
        await hostA.StartAsync(ct);
        await hostB.StartAsync(ct);

        var disposed = false;

        try
        {
            var membershipA = hostA.Services.GetRequiredService<INodeMembership>();
            var ownerA = membershipA.Identity!.Value.ToString();

            // A row A is mid-flight on when it crashes.
            var jobId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(jobId, "InFlight", (int)JobStatus.Queued, ownerA, ct);

            // Crash A: stopping the host ages out its liveness, so B classifies A as dead and emits NodeLeft(A).
            await hostA.StopAsync(ct);
            hostA.Dispose();
            disposed = true;

            // B's bridge should reclaim A's Queued row back to Idle. NodeLeft fires shortly after DeadThreshold.
            await _WaitUntilAsync(
                async () =>
                {
                    var (status, ownerId) = await fixture.ReadTimeJobAsync(jobId, ct);
                    return status == (int)JobStatus.Idle && ownerId is null;
                },
                timeout: TimeSpan.FromSeconds(20),
                ct
            );

            var (finalStatus, finalOwnerId) = await fixture.ReadTimeJobAsync(jobId, ct);
            finalStatus.Should().Be((int)JobStatus.Idle);
            finalOwnerId.Should().BeNull();
        }
        finally
        {
            if (!disposed)
            {
                await hostA.StopAsync(ct);
                hostA.Dispose();
            }

            await hostB.StopAsync(ct);
            hostB.Dispose();
        }
    }

    /// <summary>
    /// #316/U1+U2: a running job slides its own lease forward, fenced on ownership + non-terminal status. A row
    /// owned by another node renews zero rows (the cancel-on-loss signal).
    /// </summary>
    public virtual async Task running_job_renews_its_own_lease_but_a_lost_lease_renews_zero_rows()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var membership = host.Services.GetRequiredService<INodeMembership>();
            var owner = membership.Identity!.Value.ToString();

            var seededLease = DateTime.UtcNow.AddMinutes(1);
            var ownedId = Guid.NewGuid();
            var foreignId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                ownedId,
                "Owned",
                (int)JobStatus.InProgress,
                owner,
                ct,
                lockedUntil: seededLease
            );
            await fixture.SeedTimeJobAsync(
                foreignId,
                "Foreign",
                (int)JobStatus.InProgress,
                "other-node@9",
                ct,
                lockedUntil: seededLease
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            (await persistence.RenewTimeJobLeaseAsync(ownedId, ct)).Should().Be(1);
            (await persistence.RenewTimeJobLeaseAsync(foreignId, ct)).Should().Be(0); // lease lost -> cancel-on-loss

            var (ownedStatus, _, ownedLockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(ownedId, ct);
            ownedStatus.Should().Be((int)JobStatus.InProgress);
            ownedLockedUntil.Should().NotBeNull();
            ownedLockedUntil!.Value.Should().BeAfter(seededLease); // lease slid forward

            // The foreign row was not renewed: its lease is unchanged.
            (await fixture.ReadTimeJobDetailAsync(foreignId, ct))
                .LockedUntil.Should()
                .BeCloseTo(seededLease, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #461: when coordination membership is not currently established (the host hasn't started / a transient blip),
    /// the renewal returns a negative sentinel — distinct from <c>0</c> (lost) — and touches no row. The caller skips
    /// the tick rather than cancelling a healthy job.
    /// </summary>
    public virtual async Task renewal_returns_the_membership_sentinel_when_membership_is_not_established()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        // No StartAsync: coordination membership is not established, so TryGetStampOwner returns false.
        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var id = Guid.NewGuid();
        var lease = DateTime.UtcNow.AddMinutes(5);
        await fixture.SeedTimeJobAsync(id, "Job", (int)JobStatus.InProgress, "node-a@1", ct, lockedUntil: lease);

        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        // Negative sentinel (skip-the-tick), NOT 0 (lost) and NOT a renewal.
        (await persistence.RenewTimeJobLeaseAsync(id, ct))
            .Should()
            .BeNegative();

        // The row is untouched: ownership and lease are exactly as seeded (no UPDATE ran).
        var (status, ownerId, lockedUntil, _, _) = await fixture.ReadTimeJobDetailAsync(id, ct);
        status.Should().Be((int)JobStatus.InProgress);
        ownerId.Should().Be("node-a@1");
        lockedUntil.Should().BeCloseTo(lease, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// #316/U3: a job stuck InProgress whose lease lapsed is reclaimed per OnNodeDeath, independent of node death.
    /// A healthy (future-lease) row is untouched, and a second pass is idempotent.
    /// </summary>
    public virtual async Task stalled_lapsed_lease_inprogress_rows_are_reclaimed_per_policy()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string owner = "node-a@5"; // still the live node from the sweep's view — only the lease lapsed
            var lapsed = DateTime.UtcNow.AddMinutes(-1);
            var future = DateTime.UtcNow.AddMinutes(10);

            var retryId = Guid.NewGuid();
            var failId = Guid.NewGuid();
            var skipId = Guid.NewGuid();
            var healthyId = Guid.NewGuid();

            await fixture.SeedTimeJobAsync(
                retryId,
                "Retry",
                (int)JobStatus.InProgress,
                owner,
                ct,
                NodeDeathPolicy.Retry,
                lapsed
            );
            await fixture.SeedTimeJobAsync(
                failId,
                "Fail",
                (int)JobStatus.InProgress,
                owner,
                ct,
                NodeDeathPolicy.MarkFailed,
                lapsed
            );
            await fixture.SeedTimeJobAsync(
                skipId,
                "Skip",
                (int)JobStatus.InProgress,
                owner,
                ct,
                NodeDeathPolicy.Skip,
                lapsed
            );
            await fixture.SeedTimeJobAsync(
                healthyId,
                "Healthy",
                (int)JobStatus.InProgress,
                owner,
                ct,
                NodeDeathPolicy.Retry,
                future
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            (await persistence.ReclaimStalledTimeJobsAsync(ct)).Should().Be(3);

            (await fixture.ReadTimeJobAsync(retryId, ct)).Should().Be(((int)JobStatus.Idle, null));

            var (failStatus, failOwnerId, failLockedUntil, failExceptionMessage, _) =
                await fixture.ReadTimeJobDetailAsync(failId, ct);
            failStatus.Should().Be((int)JobStatus.Failed);
            failOwnerId.Should().Be(owner);
            failLockedUntil.Should().BeNull();
            failExceptionMessage.Should().Be("Lease lapsed while running!");

            var (skipStatus, _, skipLockedUntil, _, skipSkippedReason) = await fixture.ReadTimeJobDetailAsync(
                skipId,
                ct
            );
            skipStatus.Should().Be((int)JobStatus.Skipped);
            skipLockedUntil.Should().BeNull();
            skipSkippedReason.Should().Be("Lease lapsed while running!");

            // Healthy renewing job (future lease) is untouched.
            (await fixture.ReadTimeJobAsync(healthyId, ct))
                .Should()
                .Be(((int)JobStatus.InProgress, owner));

            // Idempotency: a second pass over already-reclaimed rows affects zero.
            (await persistence.ReclaimStalledTimeJobsAsync(ct))
                .Should()
                .Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // A TimeProvider whose wall clock is deliberately offset from real time, to prove the EF lease-expiry path reads
    // the DB clock rather than this node's TimeProvider (#316 clock-skew). Only GetUtcNow is exercised by the test.
    private sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }

    /// <summary>
    /// #316 clock-skew: the stalled-lease reclaim decides expiry from the DB clock, not the reclaiming node's local
    /// TimeProvider. A node whose clock runs an hour fast must NOT terminalize a lease still valid per the DB clock,
    /// yet must still reclaim a lease genuinely lapsed per the DB clock.
    /// </summary>
    public virtual async Task stalled_reclaim_uses_the_db_clock_not_a_skewed_reclaimer_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        // Reclaiming node's wall clock is 1 hour ahead of the DB. With the pre-fix TimeProvider-based comparison this
        // would make every fresh lease look lapsed; the DB-clock authority must ignore the skew. No StartAsync: the
        // reclaim is a direct persistence call and must not depend on the skewed clock driving membership.
        var skewedClock = new SkewedTimeProvider(TimeSpan.FromHours(1));

        using var host = fixture.BuildHost("node-skew", timeProvider: skewedClock);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var validId = Guid.NewGuid(); // lease valid per DB clock (~5 min out), but "lapsed" per the +1h skew
        var lapsedId = Guid.NewGuid(); // genuinely lapsed per the DB clock

        // Wide margins (+10 min valid / -5 min lapsed) so container/schema-setup latency between seed and assert can
        // never drift the DB clock enough to flip either row's lapsed/valid classification.
        await fixture.SeedTimeJobAsync(
            validId,
            "Valid",
            (int)JobStatus.InProgress,
            "node-a@1",
            ct,
            NodeDeathPolicy.MarkFailed,
            DateTime.UtcNow.AddMinutes(10)
        );
        await fixture.SeedTimeJobAsync(
            lapsedId,
            "Lapsed",
            (int)JobStatus.InProgress,
            "node-a@1",
            ct,
            NodeDeathPolicy.Retry,
            DateTime.UtcNow.AddMinutes(-5)
        );

        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        // Only the genuinely-lapsed row is reclaimed; the skewed local clock must not terminalize the still-valid
        // MarkFailed lease (pre-fix this would have returned 2 and recorded the valid row Failed mid-flight).
        (await persistence.ReclaimStalledTimeJobsAsync(ct))
            .Should()
            .Be(1);

        (await fixture.ReadTimeJobAsync(validId, ct)).Should().Be(((int)JobStatus.InProgress, "node-a@1"));
        (await fixture.ReadTimeJobAsync(lapsedId, ct)).Should().Be(((int)JobStatus.Idle, null));
    }

    /// <summary>
    /// #316 clock-skew (cron mirror): ReclaimStalledCronJobOccurrencesAsync decides expiry from the DB clock, not the
    /// reclaiming node's TimeProvider, AND terminalizes per the occurrence's OnNodeDeath. A +1h-skewed reclaimer must
    /// not touch a still-valid lease, yet must reclaim genuinely-lapsed occurrences (Retry->Idle, MarkFailed->Failed,
    /// Skip->Skipped).
    /// </summary>
    public virtual async Task cron_stalled_reclaim_uses_the_db_clock_and_terminalizes_per_policy()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        var skewedClock = new SkewedTimeProvider(TimeSpan.FromHours(1));
        using var host = fixture.BuildHost("node-skew", timeProvider: skewedClock);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);

        var cronId = Guid.NewGuid();
        await fixture.SeedCronJobAsync(cronId, "Cron", "* * * * *", NodeDeathPolicy.Retry, ct);

        var validId = Guid.NewGuid(); // valid per DB clock, "lapsed" per the +1h skew
        var retryId = Guid.NewGuid();
        var failId = Guid.NewGuid();
        var skipId = Guid.NewGuid();
        var valid = DateTime.UtcNow.AddMinutes(10);
        var lapsed = DateTime.UtcNow.AddMinutes(-5);
        // Distinct ExecutionTimes: the (CronJobId, ExecutionTime) unique index forbids duplicates.
        var baseTime = DateTime.UtcNow.AddHours(-1);

        await fixture.SeedCronOccurrenceAsync(
            validId,
            cronId,
            (int)JobStatus.InProgress,
            "n@1",
            NodeDeathPolicy.MarkFailed,
            valid,
            baseTime,
            ct
        );
        await fixture.SeedCronOccurrenceAsync(
            retryId,
            cronId,
            (int)JobStatus.InProgress,
            "n@1",
            NodeDeathPolicy.Retry,
            lapsed,
            baseTime.AddSeconds(1),
            ct
        );
        await fixture.SeedCronOccurrenceAsync(
            failId,
            cronId,
            (int)JobStatus.InProgress,
            "n@1",
            NodeDeathPolicy.MarkFailed,
            lapsed,
            baseTime.AddSeconds(2),
            ct
        );
        await fixture.SeedCronOccurrenceAsync(
            skipId,
            cronId,
            (int)JobStatus.InProgress,
            "n@1",
            NodeDeathPolicy.Skip,
            lapsed,
            baseTime.AddSeconds(3),
            ct
        );

        var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        (await persistence.ReclaimStalledCronJobOccurrencesAsync(ct)).Should().Be(3);

        (await fixture.ReadCronOccurrenceAsync(validId, ct)).Should().Be(((int)JobStatus.InProgress, "n@1"));
        (await fixture.ReadCronOccurrenceAsync(retryId, ct)).Should().Be(((int)JobStatus.Idle, null));
        (await fixture.ReadCronOccurrenceAsync(failId, ct)).Status.Should().Be((int)JobStatus.Failed);
        (await fixture.ReadCronOccurrenceAsync(skipId, ct)).Status.Should().Be((int)JobStatus.Skipped);
    }

    /// <summary>
    /// #316/#13 (cron mirror): a running occurrence slides its own lease, fenced on ownership + InProgress status. A
    /// Queued occurrence (not started) and a foreign-owned occurrence both renew zero rows (the cancel-on-loss signal).
    /// </summary>
    public virtual async Task cron_running_occurrence_renews_but_queued_or_foreign_renews_zero()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var lease = DateTime.UtcNow.AddMinutes(1);

            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "Cron", "* * * * *", NodeDeathPolicy.Retry, ct);

            var runningId = Guid.NewGuid();
            var queuedId = Guid.NewGuid();
            var foreignId = Guid.NewGuid();
            // Distinct ExecutionTimes: the (CronJobId, ExecutionTime) unique index forbids duplicates.
            var baseTime = DateTime.UtcNow.AddHours(-1);
            await fixture.SeedCronOccurrenceAsync(
                runningId,
                cronId,
                (int)JobStatus.InProgress,
                owner,
                NodeDeathPolicy.Retry,
                lease,
                baseTime,
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                queuedId,
                cronId,
                (int)JobStatus.Queued,
                owner,
                NodeDeathPolicy.Retry,
                lease,
                baseTime.AddSeconds(1),
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                foreignId,
                cronId,
                (int)JobStatus.InProgress,
                "other-node@9",
                NodeDeathPolicy.Retry,
                lease,
                baseTime.AddSeconds(2),
                ct
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            (await persistence.RenewCronJobOccurrenceLeaseAsync(runningId, ct)).Should().Be(1);
            (await persistence.RenewCronJobOccurrenceLeaseAsync(queuedId, ct)).Should().Be(0); // not running -> InProgress fence
            (await persistence.RenewCronJobOccurrenceLeaseAsync(foreignId, ct)).Should().Be(0); // not ours -> cancel-on-loss
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #5/#466 (cron mirror): UpdateCronJobOccurrenceAsync is fenced on ownership + non-terminal status and now returns the
    /// affected-row count — 1 when applied, 0 when a foreign or already-terminal occurrence is excluded.
    /// </summary>
    public virtual async Task cron_completion_is_fenced_on_ownership_and_non_terminal_status()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "Cron", "* * * * *", NodeDeathPolicy.Retry, ct);

            var baseTime = DateTime.UtcNow.AddHours(-1);
            var ownedId = Guid.NewGuid(); // ours, InProgress -> completion applies (1)
            var foreignId = Guid.NewGuid(); // other owner -> fenced (0)
            var terminalId = Guid.NewGuid(); // already terminal -> fenced (0)
            await fixture.SeedCronOccurrenceAsync(
                ownedId,
                cronId,
                (int)JobStatus.InProgress,
                owner,
                NodeDeathPolicy.Retry,
                null,
                baseTime,
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                foreignId,
                cronId,
                (int)JobStatus.InProgress,
                "other-node@9",
                NodeDeathPolicy.Retry,
                null,
                baseTime.AddSeconds(1),
                ct
            );
            await fixture.SeedCronOccurrenceAsync(
                terminalId,
                cronId,
                (int)JobStatus.Failed,
                "node-a@5",
                NodeDeathPolicy.Retry,
                null,
                baseTime.AddSeconds(2),
                ct
            );

            var owned = new JobExecutionState { FunctionName = "Cron", JobId = ownedId }.SetProperty(
                x => x.Status,
                JobStatus.Succeeded
            );
            var foreign = new JobExecutionState { FunctionName = "Cron", JobId = foreignId }.SetProperty(
                x => x.Status,
                JobStatus.Succeeded
            );
            var terminal = new JobExecutionState { FunctionName = "Cron", JobId = terminalId }.SetProperty(
                x => x.Status,
                JobStatus.Succeeded
            );

            (await persistence.UpdateCronJobOccurrenceAsync(owned, ct)).Should().Be(1);
            (await persistence.UpdateCronJobOccurrenceAsync(foreign, ct)).Should().Be(0);
            (await persistence.UpdateCronJobOccurrenceAsync(terminal, ct)).Should().Be(0);

            (await fixture.ReadCronOccurrenceAsync(ownedId, ct)).Status.Should().Be((int)JobStatus.Succeeded);
            (await fixture.ReadCronOccurrenceAsync(foreignId, ct))
                .Should()
                .Be(((int)JobStatus.InProgress, "other-node@9"));
            (await fixture.ReadCronOccurrenceAsync(terminalId, ct)).Should().Be(((int)JobStatus.Failed, "node-a@5"));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #316/U4: the dead-node sweep defers a still-leased InProgress row to the lease (recovered later by U3) but
    /// reclaims Idle/Queued rows immediately.
    /// </summary>
    public virtual async Task node_death_sweep_leaves_a_valid_lease_inprogress_row_to_the_lease()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";
            var future = DateTime.UtcNow.AddMinutes(10);

            var validId = Guid.NewGuid();
            var idleId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(validId, "Valid", (int)JobStatus.InProgress, dead, ct, lockedUntil: future);
            await fixture.SeedTimeJobAsync(idleId, "Idle", (int)JobStatus.Idle, dead, ct, lockedUntil: future);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // Only the Idle row is reclaimed now; the valid-lease InProgress row is left to the lease (U3).
            (await persistence.ReleaseDeadNodeTimeJobResourcesAsync(dead, ct))
                .Should()
                .Be(1);

            (await fixture.ReadTimeJobAsync(validId, ct)).Should().Be(((int)JobStatus.InProgress, dead));
            (await fixture.ReadTimeJobAsync(idleId, ct)).Should().Be(((int)JobStatus.Idle, null));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task queueing_a_time_job_claims_its_child_tree()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var grandChild = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "grand-child",
                RunCondition = RunCondition.OnSuccess,
            };
            var child = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "child",
                RunCondition = RunCondition.OnSuccess,
                Children = [grandChild],
            };
            var root = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "root",
                ExecutionTime = DateTime.UtcNow.AddSeconds(5),
                Children = [child],
            };
            await persistence.AddTimeJobsAsync([root], ct);
            var roots = await persistence.GetEarliestTimeJobsAsync(ct);

            var claimed = await persistence.QueueTimeJobsAsync(roots, ct).ToListAsync(ct);

            claimed.Should().ContainSingle();
            claimed[0].Status.Should().Be(JobStatus.Queued);
            claimed[0].Children.Should().ContainSingle();
            claimed[0].Children.Single().Children.Should().ContainSingle();
            (await fixture.ReadTimeJobDetailAsync(child.Id, ct)).OwnerId.Should().Be(owner);
            (await fixture.ReadTimeJobDetailAsync(grandChild.Id, ct)).OwnerId.Should().Be(owner);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task fallback_queueing_a_time_job_claims_its_child_tree()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var grandChild = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "grand-child",
                RunCondition = RunCondition.OnSuccess,
            };
            var child = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "child",
                RunCondition = RunCondition.OnSuccess,
                Children = [grandChild],
            };
            var root = new TimeJobEntity
            {
                Id = Guid.NewGuid(),
                Function = "root",
                ExecutionTime = DateTime.UtcNow.AddMinutes(-1),
                Children = [child],
            };
            await persistence.AddTimeJobsAsync([root], ct);

            var claimed = await persistence.QueueTimedOutTimeJobsAsync(ct).ToListAsync(ct);

            claimed.Should().ContainSingle();
            claimed[0].Children.Should().ContainSingle();
            claimed[0].Children.Single().Children.Should().ContainSingle();
            (await fixture.ReadTimeJobDetailAsync(child.Id, ct)).OwnerId.Should().Be(owner);
            (await fixture.ReadTimeJobDetailAsync(grandChild.Id, ct)).OwnerId.Should().Be(owner);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #316/U5 (strict Queued→InProgress fence): the unified-context start stamp promotes a Queued row owned by this
    /// node to InProgress exactly once. A duplicate same-owner scheduler wrapper cannot revalidate the now-running row —
    /// the second call matches zero rows because the row is no longer Queued. Pins the EF LINQ translation of the
    /// <c>rowsToUpdate.Where(x =&gt; x.Status == JobStatus.Queued)</c> guard against a real database.
    /// </summary>
    public virtual async Task unified_context_inprogress_stamp_requires_a_queued_row()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(id, "unified-start", (int)JobStatus.Queued, owner, ct);

            var start = new JobExecutionState { FunctionName = "unified-start" }.SetProperty(
                x => x.Status,
                JobStatus.InProgress
            );

            // First stamp promotes the Queued row to InProgress.
            (await persistence.UpdateTimeJobsWithUnifiedContextAsync([id], start, ct))
                .Should()
                .Equal(id);

            // Second stamp is a no-op: the row is InProgress, not Queued, so the strict fence excludes it.
            (await persistence.UpdateTimeJobsWithUnifiedContextAsync([id], start, ct))
                .Should()
                .BeEmpty();

            (await fixture.ReadTimeJobAsync(id, ct)).Should().Be(((int)JobStatus.InProgress, owner));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// #316/U5 (cron mirror): the cron-occurrence unified-context start stamp promotes a Queued occurrence owned by this
    /// node to InProgress exactly once; the duplicate same-owner wrapper matches zero rows on the second call. Pins the
    /// EF LINQ translation of the cron mirror's <c>rowsToUpdate.Where(x =&gt; x.Status == JobStatus.Queued)</c> guard.
    /// </summary>
    public virtual async Task cron_unified_context_inprogress_stamp_requires_a_queued_row()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().Identity!.Value.ToString();
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var cronId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "cron-start", "* * * * *", NodeDeathPolicy.Retry, ct);

            var occurrenceId = Guid.NewGuid();
            await fixture.SeedCronOccurrenceAsync(
                occurrenceId,
                cronId,
                (int)JobStatus.Queued,
                owner,
                NodeDeathPolicy.Retry,
                null,
                DateTime.UtcNow.AddHours(-1),
                ct
            );

            var start = new JobExecutionState { FunctionName = "cron-start" }.SetProperty(
                x => x.Status,
                JobStatus.InProgress
            );

            // First stamp promotes the Queued occurrence to InProgress.
            (await persistence.UpdateCronJobOccurrencesWithUnifiedContextAsync([occurrenceId], start, ct))
                .Should()
                .Equal(occurrenceId);

            // Second stamp is a no-op: the occurrence is InProgress, not Queued, so the strict fence excludes it.
            (await persistence.UpdateCronJobOccurrencesWithUnifiedContextAsync([occurrenceId], start, ct))
                .Should()
                .BeEmpty();

            (await fixture.ReadCronOccurrenceAsync(occurrenceId, ct)).Should().Be(((int)JobStatus.InProgress, owner));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private static async Task _WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }

        throw new TimeoutException($"Condition was not satisfied within {timeout}.");
    }
}
