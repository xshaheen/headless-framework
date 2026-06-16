// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
public abstract class JobsCoordinationConformanceTests<TFixture>(TFixture fixture)
    where TFixture : class, IJobsCoordinationFixture
{
    public virtual async Task queued_job_is_stamped_with_the_node_incarnation_owner()
    {
        var ct = TestContext.Current.CancellationToken;
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

            // Fetch the row with its current UpdatedAt (QueueTimeJobs uses optimistic concurrency on it), then stamp.
            var idle = await persistence.GetTimeJobs(x => x.Id == jobId, ct);
            idle.Should().ContainSingle();

            var stamped = await persistence.QueueTimeJobs(idle, ct).ToListAsync(ct);
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

    public virtual async Task reclaim_touches_only_the_dead_incarnations_non_terminal_rows()
    {
        var ct = TestContext.Current.CancellationToken;
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
            await fixture.SeedTimeJobAsync(retryInFlightId, "RetryInFlight", (int)JobStatus.InProgress, dead, ct);
            await fixture.SeedTimeJobAsync(terminalId, "Terminal", (int)JobStatus.Succeeded, dead, ct);
            await fixture.SeedTimeJobAsync(unownedId, "Unowned", (int)JobStatus.Idle, ownerId: null, ct);
            await fixture.SeedTimeJobAsync(otherIncarnationId, "Other", (int)JobStatus.Queued, "node-a@6", ct);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var affected = await persistence.ReleaseDeadNodeTimeJobResources(dead, ct);

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
        var ct = TestContext.Current.CancellationToken;
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
                NodeDeathPolicy.MarkFailed
            );

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var affected = await persistence.ReleaseDeadNodeTimeJobResources(dead, ct);

            // MarkFailed in-flight row becomes terminal Failed (never retried), owner retained for audit.
            affected.Should().Be(1);
            (await fixture.ReadTimeJobAsync(id, ct)).Should().Be(((int)JobStatus.Failed, dead));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task dead_node_with_skip_policy_transitions_in_flight_row_to_skipped()
    {
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(id, "Skip", (int)JobStatus.InProgress, dead, ct, NodeDeathPolicy.Skip);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var affected = await persistence.ReleaseDeadNodeTimeJobResources(dead, ct);

            // Skip in-flight row becomes terminal Skipped (idempotency-critical: never re-run).
            affected.Should().Be(1);
            (await fixture.ReadTimeJobAsync(id, ct)).Should().Be(((int)JobStatus.Skipped, dead));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task reclaim_is_idempotent_a_second_pass_affects_zero_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";
            await fixture.SeedTimeJobAsync(Guid.NewGuid(), "A", (int)JobStatus.Queued, dead, ct);
            await fixture.SeedTimeJobAsync(Guid.NewGuid(), "B", (int)JobStatus.InProgress, dead, ct);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            (await persistence.ReleaseDeadNodeTimeJobResources(dead, ct)).Should().Be(2);
            (await persistence.ReleaseDeadNodeTimeJobResources(dead, ct)).Should().Be(0);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task surviving_node_recovers_a_crashed_nodes_work_via_node_left_event()
    {
        var ct = TestContext.Current.CancellationToken;
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
