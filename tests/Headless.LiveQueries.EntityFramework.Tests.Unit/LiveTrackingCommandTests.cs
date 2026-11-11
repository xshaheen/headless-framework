// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveTrackingCommandTests : TestBase
{
    [Fact]
    public async Task should_re_run_the_watch_after_execute_update()
    {
        // given
        await using var host = await _CreateWithOneTodoAsync();
        await using var titles = _WatchTitles(host);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await titles.NextAsync()).Should().Equal("old");
        (await counters.NextAsync()).Should().Be(0);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await db.Todos.ExecuteUpdateAsync(todos => todos.SetProperty(todo => todo.Title, "new"), AbortToken);

        // then
        host.Wakes.Should().Be(1);
        (await titles.NextAsync()).Should().Equal("new");
        counters.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_re_run_the_watch_after_execute_delete()
    {
        // given
        await using var host = await _CreateWithOneTodoAsync();
        await using var titles = _WatchTitles(host);
        (await titles.NextAsync()).Should().Equal("old");

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await db.Todos.ExecuteDeleteAsync(AbortToken);

        // then
        host.Wakes.Should().Be(1);
        (await titles.NextAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task should_re_run_the_watch_after_raw_sql_that_quotes_the_table_as_the_provider_does()
    {
        // given
        await using var host = await _CreateWithOneTodoAsync();
        await using var titles = _WatchTitles(host);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await titles.NextAsync()).Should().Equal("old");
        (await counters.NextAsync()).Should().Be(0);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await db.Database.ExecuteSqlRawAsync("""UPDATE "Todos" SET "Title" = 'new'""", AbortToken);

        // then
        host.Wakes.Should().Be(1);
        (await titles.NextAsync()).Should().Equal("new");
        counters.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_hold_a_command_issued_inside_a_transaction_until_the_transaction_commits()
    {
        // given
        await using var host = await _CreateWithOneTodoAsync();
        await using var titles = _WatchTitles(host);
        (await titles.NextAsync()).Should().Equal("old");

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        await db.Todos.ExecuteUpdateAsync(todos => todos.SetProperty(todo => todo.Title, "new"), AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await transaction.CommitAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await titles.NextAsync()).Should().Equal("new");
    }

    [Fact]
    public async Task should_record_the_tables_a_raw_sql_query_reads()
    {
        // given - the watch reads Todos through SQL the application wrote
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var todos = host.Watch(
            (db, cancellationToken) => db.Todos.FromSqlRaw("""SELECT * FROM "Todos" """).CountAsync(cancellationToken),
            AbortToken
        );
        (await todos.NextAsync()).Should().Be(0);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.Add(new Todo { Id = 1, Title = "first" });
        await db.SaveChangesAsync(AbortToken);

        // then
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_record_a_table_under_the_key_an_application_announces_for_it()
    {
        // given - a watch that read Todos through EF Core, and a writer the tracker cannot see
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);
        var invalidator = host.Services.GetRequiredService<ILiveQueryInvalidator>();

        // when - it announces another table's key, then this table's
        await invalidator.InvalidateAsync(["table:Counters"], AbortToken);
        var wakesForAnotherTable = host.Wakes;
        await invalidator.InvalidateAsync(["table:Todos"], AbortToken);

        // then
        wakesForAnotherTable.Should().Be(0);
        host.Wakes.Should().Be(1);
    }

    private static async Task<LiveEfTestHost> _CreateWithOneTodoAsync()
    {
        var host = await LiveEfTestHost.CreateAsync();
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.Add(new Todo { Id = 1, Title = "old" });
        await db.SaveChangesAsync(AbortToken);

        return host;
    }

    private static Watch<List<string>> _WatchTitles(LiveEfTestHost host)
    {
        return host.Watch(
            (db, cancellationToken) => db.Todos.Select(todo => todo.Title).ToListAsync(cancellationToken),
            AbortToken
        );
    }
}
