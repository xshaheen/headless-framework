// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Transactions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Tracking against a real server, where every context has its own connection, a transaction is the server's, and
/// identifiers are quoted and schema-qualified the provider's way. Each test owns a host and tags its rows, so tests
/// share the tables without seeing each other's rows or announcements.
/// </summary>
public abstract class LiveTrackingProviderTests(ILiveDatabase database) : TestBase
{
    private readonly Guid _test = Guid.NewGuid();

    [Fact]
    public async Task should_re_run_the_watch_that_read_the_saved_table_and_not_the_watch_that_read_another()
    {
        // given
        await using var host = new LiveIntegrationHost(database);
        await using var items = _WatchItems(host);
        await using var orders = _WatchOrders(host);
        (await items.NextAsync()).Should().Be(0);
        (await orders.NextAsync()).Should().Be(0);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();
        db.Items.Add(_Item("first"));
        await db.SaveChangesAsync(AbortToken);

        // then
        host.Wakes.Should().Be(1);
        (await items.NextAsync()).Should().Be(1);
        orders.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_match_a_schema_qualified_table_in_queries_saves_and_raw_sql()
    {
        // given
        await using var host = new LiveIntegrationHost(database);
        await using var items = _WatchItems(host);
        await using var orders = _WatchOrders(host);
        (await items.NextAsync()).Should().Be(0);
        (await orders.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();

        // when - a save writes the table in the named schema
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Test = _test,
            Reference = "A-1",
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(AbortToken);

        // then
        host.Wakes.Should().Be(1);
        (await orders.NextAsync()).Should().Be(1);

        // when - raw SQL names the table the way the provider quotes it
#pragma warning disable EF1002 // The table name is this fixture's own constant; the row id is a parameter.
        await db.Database.ExecuteSqlRawAsync(
            $"DELETE FROM {database.OrdersTableSql} WHERE {_Quote("Id")} = {{0}}",
            [order.Id],
            AbortToken
        );
#pragma warning restore EF1002

        // then
        host.Wakes.Should().Be(2);
        (await orders.NextAsync()).Should().Be(0);
        items.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_name_a_schema_qualified_table_by_its_schema_and_name_in_the_dependency_key()
    {
        // given - a watch that read the table through EF Core
        await using var host = new LiveIntegrationHost(database);
        await using var items = _WatchItems(host);
        await using var orders = _WatchOrders(host);
        (await items.NextAsync()).Should().Be(0);
        (await orders.NextAsync()).Should().Be(0);

        // when - a writer the tracker cannot see announces the documented key
        await host.Invalidator.InvalidateAsync([LiveOrdersDb.OrdersKey], AbortToken);

        // then
        host.Wakes.Should().Be(1);
    }

    [Fact]
    public async Task should_re_run_the_watch_after_execute_update()
    {
        // given
        await using var host = new LiveIntegrationHost(database);
        var item = _Item("old");

        await using (var seed = host.CreateScope())
        {
            var seedDb = seed.ServiceProvider.GetRequiredService<LiveOrdersDb>();
            seedDb.Items.Add(item);
            await seedDb.SaveChangesAsync(AbortToken);
        }

        await using var names = host.Watch(
            (db, cancellationToken) =>
                db.Items.Where(x => x.Test == _test).Select(x => x.Name).ToListAsync(cancellationToken),
            AbortToken
        );
        await using var orders = _WatchOrders(host);
        (await names.NextAsync()).Should().Equal("old");
        (await orders.NextAsync()).Should().Be(0);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();
        await db
            .Items.Where(x => x.Test == _test)
            .ExecuteUpdateAsync(items => items.SetProperty(x => x.Name, "new"), AbortToken);

        // then
        host.Wakes.Should().Be(1);
        (await names.NextAsync()).Should().Equal("new");
        orders.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_announce_nothing_when_a_unit_of_work_saves_and_rolls_back()
    {
        // given
        await using var host = new LiveIntegrationHost(database);
        await using var items = _WatchItems(host);
        (await items.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();

        // when
        await using (var unit = await host.UnitOfWork.BeginAsync(db, AbortToken))
        {
            db.Items.Add(_Item("rolled back"));
            await db.SaveChangesAsync(AbortToken);
            await unit.RollbackAsync();
        }

        // then
        host.Wakes.Should().Be(0);
    }

    [Fact]
    public async Task should_announce_once_after_the_commit_when_a_unit_of_work_saves_twice()
    {
        // given - one watch that reads both tables the unit writes
        await using var host = new LiveIntegrationHost(database);
        await using var rows = host.Watch(
            async (db, cancellationToken) =>
                await db.Items.CountAsync(x => x.Test == _test, cancellationToken)
                + await db.Orders.CountAsync(x => x.Test == _test, cancellationToken),
            AbortToken
        );
        (await rows.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();
        await using var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);

        // when
        db.Items.Add(_Item("first save"));
        await db.SaveChangesAsync(AbortToken);
        db.Orders.Add(
            new Order
            {
                Id = Guid.NewGuid(),
                Test = _test,
                Reference = "second save",
            }
        );
        await db.SaveChangesAsync(AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await unit.CompleteAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await rows.NextAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_follow_the_outcome_of_the_transaction_scope_a_save_enlisted_in(bool completed)
    {
        // given
        await using var host = new LiveIntegrationHost(database);
        await using var items = _WatchItems(host);
        (await items.NextAsync()).Should().Be(0);
        long wakesInsideScope;

        // when - the context opens its connection inside the scope, so the server transaction is the scope's
        using (var transactionScope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var scope = host.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();
            db.Items.Add(_Item("inside the scope"));
            await db.SaveChangesAsync(AbortToken);
            wakesInsideScope = host.Wakes;

            if (completed)
            {
                transactionScope.Complete();
            }
        }

        // then - announced after a committed scope, never for an aborted one
        wakesInsideScope.Should().Be(0);
        host.Wakes.Should().Be(completed ? 1 : 0);

        if (completed)
        {
            (await items.NextAsync()).Should().Be(1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_re_run_the_watches_of_the_rows_the_database_removed_or_cleared_with_a_deleted_order(
        bool setBased
    )
    {
        // given - an order with a line the database deletes with it and a shipment the database detaches from it
        await using var host = new LiveIntegrationHost(database);
        var orderId = Guid.NewGuid();

        await host.WriteAsync(async db =>
        {
            db.Orders.Add(_Order(orderId));
            db.OrderLines.Add(
                new OrderLine
                {
                    Id = Guid.NewGuid(),
                    Test = _test,
                    OrderId = orderId,
                }
            );
            db.Shipments.Add(
                new Shipment
                {
                    Id = Guid.NewGuid(),
                    Test = _test,
                    OrderId = orderId,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });

        await using var lines = host.Watch(
            (db, cancellationToken) => db.OrderLines.CountAsync(x => x.Test == _test, cancellationToken),
            AbortToken
        );
        await using var shipped = host.Watch(
            (db, cancellationToken) =>
                db.Shipments.CountAsync(x => x.Test == _test && x.OrderId != null, cancellationToken),
            AbortToken
        );
        await using var items = _WatchItems(host);
        (await lines.NextAsync()).Should().Be(1);
        (await shipped.NextAsync()).Should().Be(1);
        (await items.NextAsync()).Should().Be(0);

        // when - the order is deleted alone, by a save that never loaded the other rows or by ExecuteDelete
        await host.WriteAsync(async db =>
        {
            if (setBased)
            {
                await db.Orders.Where(x => x.Id == orderId).ExecuteDeleteAsync(AbortToken);
            }
            else
            {
                db.Orders.Remove(_Order(orderId));
                await db.SaveChangesAsync(AbortToken);
            }
        });

        // then - no command named the two tables; the server changed them
        host.Wakes.Should().Be(2);
        (await lines.NextAsync()).Should().Be(0);
        (await shipped.NextAsync()).Should().Be(0);
        items.Runs.Should().Be(1);
    }

    /// <summary>Quotes one identifier the provider's way.</summary>
    protected abstract string QuoteIdentifier(string name);

    private string _Quote(string name) => QuoteIdentifier(name);

    private Item _Item(string name)
    {
        return new Item
        {
            Id = Guid.NewGuid(),
            Test = _test,
            Name = name,
        };
    }

    private Order _Order(Guid id)
    {
        return new Order
        {
            Id = id,
            Test = _test,
            Reference = "A-1",
        };
    }

    private Watch<int> _WatchItems(LiveIntegrationHost host)
    {
        return host.Watch(
            (db, cancellationToken) => db.Items.CountAsync(x => x.Test == _test, cancellationToken),
            AbortToken
        );
    }

    private Watch<int> _WatchOrders(LiveIntegrationHost host)
    {
        return host.Watch(
            (db, cancellationToken) => db.Orders.CountAsync(x => x.Test == _test, cancellationToken),
            AbortToken
        );
    }
}

[Collection<PostgreSqlLiveFixture>]
public sealed class PostgreSqlLiveTrackingTests(PostgreSqlLiveFixture fixture) : LiveTrackingProviderTests(fixture)
{
    protected override string QuoteIdentifier(string name) => $"\"{name}\"";

    // PostgreSQL only: SQL Server refuses to truncate a table a foreign key refers to.
    [Fact]
    public async Task should_re_run_the_watches_of_the_rows_the_database_removed_with_a_truncated_table()
    {
        // given - an order with a line and a shipment that refer to it
        await using var host = new LiveIntegrationHost(fixture);
        var test = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await host.WriteAsync(async db =>
        {
            db.Orders.Add(
                new Order
                {
                    Id = orderId,
                    Test = test,
                    Reference = "A-1",
                }
            );
            db.OrderLines.Add(
                new OrderLine
                {
                    Id = Guid.NewGuid(),
                    Test = test,
                    OrderId = orderId,
                }
            );
            db.Shipments.Add(
                new Shipment
                {
                    Id = Guid.NewGuid(),
                    Test = test,
                    OrderId = orderId,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });

        await using var lines = host.Watch(
            (db, cancellationToken) => db.OrderLines.CountAsync(x => x.Test == test, cancellationToken),
            AbortToken
        );
        await using var shipments = host.Watch(
            (db, cancellationToken) => db.Shipments.CountAsync(x => x.Test == test, cancellationToken),
            AbortToken
        );
        (await lines.NextAsync()).Should().Be(1);
        (await shipments.NextAsync()).Should().Be(1);

        // when - the statement names the orders alone, and the server empties every table that refers to them
#pragma warning disable EF1002 // The table name is this fixture's own constant.
        await host.WriteAsync(async db =>
            await db.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE {fixture.OrdersTableSql} CASCADE", AbortToken)
        );
#pragma warning restore EF1002

        // then
        host.Wakes.Should().Be(2);
        (await lines.NextAsync()).Should().Be(0);
        (await shipments.NextAsync()).Should().Be(0);
    }
}

[Collection<SqlServerLiveFixture>]
public sealed class SqlServerLiveTrackingTests(SqlServerLiveFixture fixture) : LiveTrackingProviderTests(fixture)
{
    protected override string QuoteIdentifier(string name) => $"[{name}]";
}
