// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Provider-neutral due-time behavior of the time-job peek, the read the scheduler sleeps on before it claims at the
/// wake, and of the immediate acquire that enqueue uses.
/// Every instant is seeded on the store clock, so a node clock that leads or lags the store shows up only through
/// the host's <see cref="TimeProvider"/>.
/// </summary>
public abstract class JobsPeekConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    /// <summary>
    /// A row due two seconds from now on the store clock must be peeked by a node whatever its own clock says. Bounding
    /// the peek on the node clock hid it from a node five seconds ahead (node now − 1s is store now + 4s), so that node
    /// never woke for it and the row waited for the fallback sweep.
    /// </summary>
    public virtual async Task peek_returns_a_row_due_on_the_store_clock_whatever_the_node_clock_says(
        int nodeSkewSeconds
    )
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "peek-skew",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromSeconds(nodeSkewSeconds))
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(id, "peek-skew", (int)JobStatus.Idle, null, ct);
            await _SetExecutionTimeOnStoreClockAsync([id], offsetSeconds: 2, ct);

            var peek = await persistence.GetEarliestTimeJobsAsync(ct);

            peek.Jobs.Select(x => x.Id).Should().Equal(id);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// An absolute due time is stored as given, so a node whose clock lags the store and schedules a job for its own now
    /// writes a row the store already sees as more than a second overdue. Immediate dispatch leaves a cluster-limited
    /// job for the scheduler it wakes, so the peek must still return that row; bounding it on the store clock alone
    /// left it to the fallback sweep.
    /// </summary>
    public virtual async Task peek_returns_an_absolute_time_a_lagging_node_set_to_its_own_now()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        var nodeClock = new SkewedTimeProvider(TimeSpan.FromSeconds(-5));
        using var host = fixture.BuildHost("peek-lag", timeProvider: nodeClock);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var id = await scheduler.ScheduleAsync<CoordinatedJob>(nodeClock.GetUtcNow(), ct);

            var peek = await persistence.GetEarliestTimeJobsAsync(ct);

            peek.Jobs.Select(x => x.Id).Should().Equal(id);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// A run-now job is stamped by the store, so the peek finds it through the store-clock bound whichever way the
    /// enqueuing node's clock is off. A node two minutes ahead bounds its own window past that stamp, so only the
    /// store-clock bound can return the row there.
    /// </summary>
    public virtual async Task peek_returns_a_run_now_row_a_skewed_node_enqueued(int nodeSkewSeconds)
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "peek-run-now",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromSeconds(nodeSkewSeconds))
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();
            var id = await scheduler.EnqueueAsync<CoordinatedJob>(ct);

            var peek = await persistence.GetEarliestTimeJobsAsync(ct);

            peek.Jobs.Select(x => x.Id).Should().Equal(id);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// Immediate dispatch asks for a row when this node's clock says it is due. A node a minute ahead of the store would
    /// otherwise start a job scheduled 30 seconds out at once; the store's own clock must decide, as it does for the
    /// scheduler's claim.
    /// </summary>
    public virtual async Task immediate_acquire_takes_only_rows_due_on_the_store_clock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost(
            "acquire-lead",
            timeProvider: new SkewedTimeProvider(TimeSpan.FromSeconds(60))
        );
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var dueId = Guid.NewGuid();
            var futureId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(dueId, "acquire-lead", (int)JobStatus.Idle, null, ct);
            await fixture.SeedTimeJobAsync(futureId, "acquire-lead", (int)JobStatus.Idle, null, ct);
            await _SetExecutionTimeOnStoreClockAsync([dueId], offsetSeconds: -1, ct);
            await _SetExecutionTimeOnStoreClockAsync([futureId], offsetSeconds: 30, ct);

            var acquired = await persistence.AcquireImmediateTimeJobsAsync([dueId, futureId], ct);

            acquired.Select(x => x.Id).Should().Equal(dueId);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    /// <summary>
    /// A row this node already holds was dispatched by an earlier wake; peeking it again would admit it to the pool a
    /// second time. A row whose foreign lease lapsed is free for anyone, so the peek keeps returning it.
    /// </summary>
    public virtual async Task peek_skips_a_row_this_node_holds_and_returns_a_row_whose_foreign_lease_lapsed()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("peek-owner");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            host.Services.GetRequiredService<IJobsOwnerIdentity>().TryGetStampOwner(out var owner).Should().BeTrue();

            var heldId = Guid.NewGuid();
            var lapsedId = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(heldId, "peek-owner", (int)JobStatus.Queued, owner, ct);
            await fixture.SeedTimeJobAsync(lapsedId, "peek-owner", (int)JobStatus.Idle, "foreign@1", ct);
            await _SetLockedUntilOnStoreClockAsync(heldId, offsetSeconds: 300, ct);
            await _SetLockedUntilOnStoreClockAsync(lapsedId, offsetSeconds: -60, ct);

            // One statement, so both rows share one execution instant and fall in the same peek window.
            await _SetExecutionTimeOnStoreClockAsync([heldId, lapsedId], offsetSeconds: 2, ct);

            var peek = await persistence.GetEarliestTimeJobsAsync(ct);

            peek.Jobs.Select(x => x.Id).Should().Equal(lapsedId);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    private async Task _SetExecutionTimeOnStoreClockAsync(Guid[] ids, int offsetSeconds, CancellationToken ct)
    {
        var parameters = ids.Select((_, index) => $"@id{index}").ToArray();
        await _ExecuteSqlAsync(
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"ExecutionTime\" = {fixture.UtcNowOffsetSqlExpression(offsetSeconds)} "
                + $"WHERE \"Id\" IN ({string.Join(", ", parameters)});",
            ct,
            [.. ids.Select((id, index) => (parameters[index], (object)id))]
        );
    }

    private Task _SetLockedUntilOnStoreClockAsync(Guid id, int offsetSeconds, CancellationToken ct)
    {
        return _ExecuteSqlAsync(
            $"UPDATE {fixture.QualifiedTimeJobsTable} SET \"LockedUntil\" = {fixture.UtcNowOffsetSqlExpression(offsetSeconds)} WHERE \"Id\" = @id;",
            ct,
            ("@id", id)
        );
    }

    private async Task _ExecuteSqlAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters
    )
    {
        await using var connection = fixture.CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = fixture.Sql(sql);
        foreach (var (name, value) in parameters)
        {
            JobsCoordinationFixtureExtensions.AddParameter(command, name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.Add(offset);
    }
}
