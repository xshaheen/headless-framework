// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// What one lease-renewal tick of a running job costs the store. Every running job renews on each
/// <c>LeaseRenewalInterval</c>, so a fleet's renewal load is this cost times its running jobs: the renewal of a running
/// row must be a single statement, not a probe plus a transaction around the fenced update.
/// </summary>
public abstract class JobsLeaseRenewalCostConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    /// <summary>Counts the round trips a store makes on the connections the Jobs context creates.</summary>
    protected abstract JobsRoundTripMeter CreateMeter();

    public virtual async Task a_running_time_job_renews_its_lease_without_a_probe_or_a_transaction()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var meter = CreateMeter();
        using var host = fixture.BuildHost("renewal-cost", interceptor: meter);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().GetOwnerTag()!;
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(
                id,
                "Job",
                (int)JobStatus.InProgress,
                owner,
                ct,
                // wall-clock: the real host renews against the system clock, so the seeded lease must lie in its future.
                lockedUntil: DateTime.UtcNow.AddMinutes(5)
            );
            var store = _Store(host);

            // Warm the context model and the pool so the measured call counts only its own statements.
            (await store.RenewTimeJobLeaseAsync(JobsLeaseTables.TimeJob(id, 1), owner, ct))
                .Should()
                .Be(1);

            var cost = await meter.MeasureAsync(() =>
                store.RenewTimeJobLeaseAsync(JobsLeaseTables.TimeJob(id, 1), owner, ct)
            );

            // A tick of a running time job renews the lease and reads its durable cancellation request.
            var tick = await meter.MeasureAsync(async () =>
            {
                var renewed = await store.RenewTimeJobLeaseAsync(JobsLeaseTables.TimeJob(id, 1), owner, ct);
                (await store.IsTimeJobCancellationRequestedAsync(id, ct)).Should().BeFalse();

                return renewed;
            });

            _Report("time job", cost);
            _Report("time job tick", tick);
            cost.Result.Should().Be(1);
            // The renewal itself, then the slide of the continuations waiting under the run's claim.
            cost.Commands.Should().Be(2);
            // No transaction of its own: each statement is one round trip, with no BEGIN or COMMIT around it.
            (cost.RoundTrips ?? cost.Commands)
                .Should()
                .Be(cost.Commands);
            tick.Result.Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    public virtual async Task a_running_cron_occurrence_renews_its_lease_in_one_statement_without_a_transaction()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var meter = CreateMeter();
        using var host = fixture.BuildHost("renewal-cost-cron", interceptor: meter);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var owner = host.Services.GetRequiredService<INodeMembership>().GetOwnerTag()!;
            var cronId = Guid.NewGuid();
            var occurrenceId = Guid.NewGuid();
            await fixture.SeedCronJobAsync(cronId, "Cron", "* * * * *", NodeDeathPolicy.Retry, ct);
            await fixture.SeedCronOccurrenceAsync(
                occurrenceId,
                cronId,
                (int)JobStatus.InProgress,
                owner,
                NodeDeathPolicy.Retry,
                // wall-clock: the real host renews against the system clock, so the seeded lease must lie in its future.
                DateTime.UtcNow.AddMinutes(5),
                DateTime.UtcNow.AddHours(-1), // wall-clock: an occurrence already due on the host's system clock.
                ct
            );
            var store = _Store(host);
            var row = JobsLeaseTables.CronOccurrence(occurrenceId, 1);

            (await store.RenewCronJobOccurrenceLeaseAsync(row, owner, ct)).Should().Be(1);

            var cost = await meter.MeasureAsync(() => store.RenewCronJobOccurrenceLeaseAsync(row, owner, ct));

            _Report("cron occurrence", cost);
            cost.Result.Should().Be(1);
            cost.Commands.Should().Be(1);
            // No transaction of its own: each statement is one round trip, with no BEGIN or COMMIT around it.
            (cost.RoundTrips ?? cost.Commands)
                .Should()
                .Be(cost.Commands);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private static void _Report(string kind, JobsStoreCost cost)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Renewing a {kind}: {cost.Commands} commands, {cost.RoundTrips?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a"} round trips"
        );
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(IHost host) =>
        host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
}

/// <summary>The store work one call cost, as a provider's client counts it.</summary>
/// <param name="Result">What the measured call returned.</param>
/// <param name="Commands">Statements (batches) the client sent.</param>
/// <param name="RoundTrips">Server round trips, where the client counts them; otherwise <see langword="null"/>.</param>
public sealed record JobsStoreCost(int Result, long Commands, long? RoundTrips);

/// <summary>
/// Counts what a call costs on the connections the Jobs context creates. Jobs' lease transitions run raw commands off
/// the context's connection, past EF's command interception, so a meter counts at the ADO.NET client instead.
/// </summary>
public abstract class JobsRoundTripMeter : DbConnectionInterceptor, IDisposable
{
    /// <summary>Runs <paramref name="call"/> and reports what it cost.</summary>
    public abstract Task<JobsStoreCost> MeasureAsync(Func<Task<int>> call);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }
}
