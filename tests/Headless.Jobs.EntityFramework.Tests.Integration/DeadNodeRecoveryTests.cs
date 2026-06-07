// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// R2/R3/R4: dead-node reclaim against real SQL. The strict <c>WhereOwnedBy</c> predicate touches only the dead
/// incarnation's non-terminal rows; reclaim is idempotent; and a surviving node recovers a crashed node's work
/// end-to-end through the coordination <c>NodeLeft</c> event — with no Redis in the wiring (R3).
/// </summary>
[Collection<JobsCoordinationFixture>]
public sealed class DeadNodeRecoveryTests(JobsCoordinationFixture fixture)
{
    [Fact]
    public async Task reclaim_touches_only_the_dead_incarnations_non_terminal_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixture.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            const string dead = "node-a@5";

            var reclaimedId = Guid.NewGuid(); // dead owner, Queued -> released back to Idle
            var skippedId = Guid.NewGuid(); // dead owner, InProgress -> Skipped
            var terminalId = Guid.NewGuid(); // dead owner, Done -> untouched (terminal)
            var unownedId = Guid.NewGuid(); // no owner, Idle -> untouched
            var otherIncarnationId = Guid.NewGuid(); // node-a@6, Queued -> untouched (different incarnation)

            await fixture.SeedTimeJobAsync(reclaimedId, "Reclaimed", (int)JobStatus.Queued, dead, ct);
            await fixture.SeedTimeJobAsync(skippedId, "Skipped", (int)JobStatus.InProgress, dead, ct);
            await fixture.SeedTimeJobAsync(terminalId, "Terminal", (int)JobStatus.Done, dead, ct);
            await fixture.SeedTimeJobAsync(unownedId, "Unowned", (int)JobStatus.Idle, lockHolder: null, ct);
            await fixture.SeedTimeJobAsync(otherIncarnationId, "Other", (int)JobStatus.Queued, "node-a@6", ct);

            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            var affected = await persistence.ReleaseDeadNodeTimeJobResources(dead, ct);

            // One Queued row released + one InProgress row skipped.
            affected.Should().Be(2);

            (await fixture.ReadTimeJobAsync(reclaimedId, ct)).Should().Be(((int)JobStatus.Idle, null));
            (await fixture.ReadTimeJobAsync(skippedId, ct)).Should().Be(((int)JobStatus.Skipped, dead));
            (await fixture.ReadTimeJobAsync(terminalId, ct)).Should().Be(((int)JobStatus.Done, dead));
            (await fixture.ReadTimeJobAsync(unownedId, ct)).Should().Be(((int)JobStatus.Idle, null));
            (await fixture.ReadTimeJobAsync(otherIncarnationId, ct)).Should().Be(((int)JobStatus.Queued, "node-a@6"));
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    [Fact]
    public async Task reclaim_is_idempotent_a_second_pass_affects_zero_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);

        using var host = fixture.BuildHost("node-a");
        await JobsCoordinationFixture.CreateJobsSchemaAsync(host, ct);
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

    [Fact]
    public async Task surviving_node_recovers_a_crashed_nodes_work_via_node_left_event()
    {
        var ct = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(ct);

        // Two nodes in one cluster. No Redis anywhere in the wiring — recovery flows purely through the
        // coordination NodeLeft event into the Jobs MembershipRecoveryBridge (R3).
        var hostA = fixture.BuildHost("node-a");
        var hostB = fixture.BuildHost("node-b");

        await JobsCoordinationFixture.CreateJobsSchemaAsync(hostA, ct);
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
                    var (status, lockHolder) = await fixture.ReadTimeJobAsync(jobId, ct);
                    return status == (int)JobStatus.Idle && lockHolder is null;
                },
                timeout: TimeSpan.FromSeconds(20),
                ct
            );

            var (finalStatus, finalLockHolder) = await fixture.ReadTimeJobAsync(jobId, ct);
            finalStatus.Should().Be((int)JobStatus.Idle);
            finalLockHolder.Should().BeNull();
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
