// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Provider-neutral behavior of the time-job peek, the read the scheduler sleeps on before it claims at the wake.
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
