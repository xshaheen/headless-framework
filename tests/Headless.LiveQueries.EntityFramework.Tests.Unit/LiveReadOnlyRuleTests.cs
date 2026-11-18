// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveReadOnlyRuleTests : TestBase
{
    [Fact]
    public async Task should_fail_a_live_query_that_calls_save_changes_and_store_nothing()
    {
        // given
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var watch = host.Watch(
            async (db, cancellationToken) =>
            {
                db.Todos.Add(new Todo { Id = 1, Title = "written by a query" });
                await db.SaveChangesAsync(cancellationToken);

                return await db.Todos.CountAsync(cancellationToken);
            },
            AbortToken
        );

        // when
        var run = watch.NextAsync;

        // then
        await run.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*SaveChanges*table:Todos*must be read-only*");
        (await _CountTodosAsync(host)).Should().Be(0);
    }

    [Fact]
    public async Task should_fail_a_live_query_that_calls_execute_update()
    {
        await using var host = await _CreateWithOneTodoAsync();
        await using var watch = host.Watch(
            (db, cancellationToken) =>
                db.Todos.ExecuteUpdateAsync(
                    todos => todos.SetProperty(todo => todo.Title, "changed"),
                    cancellationToken
                ),
            AbortToken
        );

        var run = watch.NextAsync;

        await run.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ExecuteUpdate or ExecuteDelete*table:Todos*must be read-only*");
        (await _ReadTitleAsync(host)).Should().Be("old");
    }

    [Fact]
    public async Task should_fail_a_live_query_that_calls_execute_delete()
    {
        await using var host = await _CreateWithOneTodoAsync();
        await using var watch = host.Watch(
            (db, cancellationToken) => db.Todos.ExecuteDeleteAsync(cancellationToken),
            AbortToken
        );

        var run = watch.NextAsync;

        await run.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ExecuteUpdate or ExecuteDelete*table:Todos*must be read-only*");
        (await _CountTodosAsync(host)).Should().Be(1);
    }

    [Fact]
    public async Task should_fail_a_live_query_that_writes_a_tracked_table_with_raw_sql()
    {
        await using var host = await _CreateWithOneTodoAsync();
        await using var watch = host.Watch(
            (db, cancellationToken) => db.Database.ExecuteSqlRawAsync("""DELETE FROM "Todos" """, cancellationToken),
            AbortToken
        );

        var run = watch.NextAsync;

        await run.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ExecuteSql*table:Todos*must be read-only*");
        (await _CountTodosAsync(host)).Should().Be(1);
    }

    [Fact]
    public async Task should_let_the_same_writes_through_outside_a_live_query()
    {
        // given - a watch has run on this flow, so its recorder must not linger after the run
        await using var host = await _CreateWithOneTodoAsync();
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(1);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var deleted = await db.Todos.ExecuteDeleteAsync(AbortToken);

        // then
        deleted.Should().Be(1);
        (await todos.NextAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_announce_and_not_refuse_a_write_by_work_that_outlived_the_run_that_started_it()
    {
        // given - the query starts work that is still running when the run returns, as a lazily started loop does
        await using var host = await _CreateWithOneTodoAsync();
        var runReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? leftover = null;
        await using var todos = host.Watch(
            async (db, cancellationToken) =>
            {
                leftover ??= Task.Run(
                    async () =>
                    {
                        await runReturned.Task;
                        await host.WriteAsync(async writer =>
                        {
                            writer.Todos.Add(new Todo { Id = 2, Title = "written after the run" });
                            await writer.SaveChangesAsync(AbortToken);
                        });
                    },
                    AbortToken
                );

                return await db.Todos.CountAsync(cancellationToken);
            },
            AbortToken
        );
        (await todos.NextAsync()).Should().Be(1);

        // when
        runReturned.SetResult();
        var write = () => leftover!;

        // then - the write is an ordinary one: it is stored, and the query that reads the table runs again
        await write.Should().NotThrowAsync();
        (await todos.NextAsync()).Should().Be(2);
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

    private static async Task<int> _CountTodosAsync(LiveEfTestHost host)
    {
        await using var scope = host.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<LiveDb>().Todos.CountAsync(AbortToken);
    }

    private static async Task<string> _ReadTitleAsync(LiveEfTestHost host)
    {
        await using var scope = host.CreateScope();

        return await scope
            .ServiceProvider.GetRequiredService<LiveDb>()
            .Todos.Select(todo => todo.Title)
            .SingleAsync(AbortToken);
    }
}
