// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests;

/// <summary>
/// A lease kept on the context's own rows: the descriptor the model builds drives the transitions on the context's
/// connection and transaction, and the lease queries select by the database clock, on each provider's schema as EF Core
/// created it.
/// </summary>
public abstract class FencedRowEntityFrameworkTests(IWorkDatabase database) : TestBase
{
    private static readonly LeaseGrantOptions _Grant = new() { Holder = "worker" };

    /// <summary>The SQL of the lease state column as the provider's query names it.</summary>
    protected abstract string StateColumnSql { get; }

    /// <summary>The SQL that compares the expiry with the database clock.</summary>
    protected abstract string ClockComparisonSql { get; }

    [Fact]
    public async Task should_settle_the_current_generation_and_refuse_a_stale_one_on_the_contexts_transaction()
    {
        var id = Guid.NewGuid();
        await _InsertAsync(id);

        await using (var context = database.CreateContext())
        {
            var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, context.Dialect);
            await context.Database.OpenConnectionAsync(AbortToken);
            var connection = context.Database.GetDbConnection();

            var stale = await _GrantAsync(table, connection, id);
            (await FencedRows.ReleaseAsync(table, connection, null, stale, cancellationToken: AbortToken))
                .Should()
                .Be(LeaseSettlementStatus.Released);
            var current = await _GrantAsync(table, connection, id);
            current.Generation.Should().BeGreaterThan(stale.Generation);

            await using var transaction = await context.Database.BeginTransactionAsync(AbortToken);
            var item = await context.WorkItems.SingleAsync(w => w.Id == id, AbortToken);
            item.Lease.Generation.Should().Be(current.Generation, "the context reads the lease the grant wrote");
            item.Status = "done";
            await context.SaveChangesAsync(AbortToken);

            var dbTransaction = context.Database.CurrentTransaction!.GetDbTransaction();
            (await FencedRows.SettleAsync(table, connection, dbTransaction, stale, cancellationToken: AbortToken))
                .Should()
                .Be(LeaseSettlementStatus.Stale);
            (await FencedRows.SettleAsync(table, connection, dbTransaction, current, cancellationToken: AbortToken))
                .Should()
                .Be(LeaseSettlementStatus.Settled);

            await transaction.CommitAsync(AbortToken);
        }

        await using var verify = database.CreateContext();
        var stored = await verify.WorkItems.AsNoTracking().SingleAsync(w => w.Id == id, AbortToken);
        stored.Status.Should().Be("done");
        stored.Lease.State.Should().Be(RowLeaseState.Settled, "saving the entity never overwrites the lease columns");
        stored.Lease.Holder.Should().Be("worker");
    }

    [Fact]
    public async Task should_keep_the_stored_lease_when_an_entity_loaded_before_the_grant_is_saved()
    {
        var id = Guid.NewGuid();
        await _InsertAsync(id);

        await using var context = database.CreateContext();

        // The entity is tracked with the idle lease it had before the grant. EF Core writes a property only when it
        // differs from that loaded snapshot, so the entity's lease is changed too: a save that wrote the lease
        // columns would put this made-up lease over the grant.
        var item = await context.WorkItems.SingleAsync(w => w.Id == id, AbortToken);
        item.Lease.State.Should().Be(RowLeaseState.Idle);

        var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, context.Dialect);
        await context.Database.OpenConnectionAsync(AbortToken);
        var granted = await _GrantAsync(table, context.Database.GetDbConnection(), id);

        item.Status = "running";
        item.Lease = item.Lease with
        {
            Generation = granted.Generation + 7,
            State = RowLeaseState.Released,
            Holder = "stale-entity",
        };
        await context.SaveChangesAsync(AbortToken);

        await using var verify = database.CreateContext();
        var stored = await verify.WorkItems.AsNoTracking().SingleAsync(w => w.Id == id, AbortToken);
        stored.Status.Should().Be("running");
        stored.Lease.State.Should().Be(RowLeaseState.Active, "saving the entity never overwrites the lease columns");
        stored.Lease.Generation.Should().Be(granted.Generation);
        stored.Lease.Holder.Should().Be("worker");
    }

    [Fact]
    public async Task should_select_live_and_grantable_rows_by_the_database_clock()
    {
        var idle = Guid.NewGuid();
        var active = Guid.NewGuid();
        var expired = Guid.NewGuid();
        Guid[] ids = [idle, active, expired];

        foreach (var id in ids)
        {
            await _InsertAsync(id);
        }

        await using var context = database.CreateContext();
        var table = context.FencedRowTable<WorkItem, Guid>(w => w.Lease, context.Dialect);
        await context.Database.OpenConnectionAsync(AbortToken);
        var connection = context.Database.GetDbConnection();
        await _GrantAsync(table, connection, active);
        await _GrantAsync(table, connection, expired);

        // An hour on the app clock keeps the row expired by the database clock whatever the skew between the two.
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        await context
            .WorkItems.Where(w => w.Id == expired)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Lease.ExpiresAt, past), AbortToken);

        var live = context.WorkItems.Where(w => ids.Contains(w.Id)).WhereLeaseLive(w => w.Lease);
        var grantable = context.WorkItems.Where(w => ids.Contains(w.Id)).WhereLeaseGrantable(w => w.Lease);

        live.ToQueryString().Should().Contain(StateColumnSql).And.Contain(ClockComparisonSql);
        grantable.ToQueryString().Should().Contain(StateColumnSql).And.Contain(ClockComparisonSql);
        (await live.Select(w => w.Id).ToListAsync(AbortToken)).Should().BeEquivalentTo([active]);
        (await grantable.Select(w => w.Id).ToListAsync(AbortToken)).Should().BeEquivalentTo([idle, expired]);
    }

    private async Task _InsertAsync(Guid id)
    {
        await using var context = database.CreateContext();
        context.WorkItems.Add(new WorkItem { Id = id });
        await context.SaveChangesAsync(AbortToken);
    }

    private static async Task<FencedRow<Guid>> _GrantAsync(
        FencedRowTable<Guid> table,
        System.Data.Common.DbConnection connection,
        Guid id
    )
    {
        var grant = await FencedRows.GrantAsync(
            table,
            connection,
            null,
            id,
            TimeSpan.FromMinutes(5),
            _Grant,
            AbortToken
        );

        grant.Should().NotBeNull("the row exists");
        grant!.IsAcquired.Should().BeTrue();

        return grant.Row!;
    }
}

[Collection<PostgreSqlWorkFixture>]
public sealed class PostgreSqlFencedRowEntityFrameworkTests(PostgreSqlWorkFixture fixture)
    : FencedRowEntityFrameworkTests(fixture)
{
    protected override string StateColumnSql => "w.lease_state";

    protected override string ClockComparisonSql => "w.lease_expires_at > clock_timestamp()";
}

[Collection<SqlServerWorkFixture>]
public sealed class SqlServerFencedRowEntityFrameworkTests(SqlServerWorkFixture fixture)
    : FencedRowEntityFrameworkTests(fixture)
{
    protected override string StateColumnSql => "[w].[LeaseState]";

    protected override string ClockComparisonSql => "[w].[LeaseExpiresAt] > TODATETIMEOFFSET(SYSUTCDATETIME(), 0)";
}

[Collection<SqliteWorkFixture>]
public sealed class SqliteFencedRowEntityFrameworkTests(SqliteWorkFixture fixture)
    : FencedRowEntityFrameworkTests(fixture)
{
    protected override string StateColumnSql => "\"w\".\"lease_state\"";

    protected override string ClockComparisonSql =>
        "\"w\".\"lease_expires_at\" > (strftime('%Y-%m-%d %H:%M:%f', 'now') || '000+00:00')";
}
