// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveTrackingSaveTests : TestBase
{
    [Fact]
    public async Task should_re_run_the_watch_that_read_the_saved_table_and_not_the_watch_that_read_another()
    {
        // given
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var todos = host.WatchTodoCount(AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);
        (await counters.NextAsync()).Should().Be(0);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.Add(new Todo { Id = 1, Title = "write the report" });
        await db.SaveChangesAsync(AbortToken);

        // then - one announcement signalled one watch, and it was the one that read Todos
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(1);
        counters.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_announce_a_save_only_after_the_transaction_ef_core_opened_for_it_has_committed()
    {
        // given - two rows, so EF Core wraps the save in a transaction of its own
        var beforeCommit = new BeforeCommitProbe();
        await using var host = await LiveEfTestHost.CreateAsync(options => options.AddInterceptors(beforeCommit));
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);
        beforeCommit.Read = () => host.Wakes;

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.AddRange(new Todo { Id = 1, Title = "first" }, new Todo { Id = 2, Title = "second" });
        await db.SaveChangesAsync(AbortToken);

        // then - nothing was announced when the commit was about to be sent, and one announcement followed it
        beforeCommit.Seen.Should().Equal(0);
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task should_announce_only_the_next_saves_tables_after_a_save_that_failed_or_was_cancelled(
        bool cancelled,
        bool inTransaction
    )
    {
        // given - a todo already stored, so a second todo with its id fails the save
        await using var host = await LiveEfTestHost.CreateAsync();
        await _SeedTodoAsync(host, id: 1, title: "stored");
        await using var todos = host.WatchTodoCount(AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await todos.NextAsync()).Should().Be(1);
        (await counters.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var transaction = inTransaction ? await db.Database.BeginTransactionAsync(AbortToken) : null;

        // when - the save of a todo does not happen
        if (cancelled)
        {
            db.Todos.Add(new Todo { Id = 2, Title = "never stored" });
            var save = () => db.SaveChangesAsync(new CancellationToken(canceled: true));
            await save.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            db.Todos.Add(new Todo { Id = 1, Title = "duplicate" });
            var save = () => db.SaveChangesAsync(AbortToken);
            await save.Should().ThrowAsync<DbUpdateException>();
        }

        host.Wakes.Should().Be(0);

        // and the same context then saves a counter
        db.ChangeTracker.Clear();
        db.Counters.Add(new Counter { Id = 1, Value = 1 });
        await db.SaveChangesAsync(AbortToken);

        if (transaction is not null)
        {
            host.Wakes.Should().Be(0);
            await transaction.CommitAsync(AbortToken);
        }

        // then - only the watch on Counters was signalled
        host.Wakes.Should().Be(1);
        (await counters.NextAsync()).Should().Be(1);
        todos.Runs.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_announce_a_save_once_when_the_commit_of_its_own_transaction_throws(bool trackerRunsFirst)
    {
        // given - two rows, so EF Core opens a transaction, whose commit is applied and then loses its answer; the
        // tracker hears of the commit, then of its failure, when it runs before the interceptor that throws
        var answerLost = new CommitAnswerLostInterceptor();
        await using var host = await LiveEfTestHost.CreateAsync(
            options => options.AddInterceptors(answerLost),
            trackerRunsFirst: trackerRunsFirst
        );
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.AddRange(new Todo { Id = 1, Title = "first" }, new Todo { Id = 2, Title = "second" });
        answerLost.Armed = true;

        // when
        var save = () => db.SaveChangesAsync(AbortToken);

        // then - the rows may be stored, so the watch is signalled, once, and finds them
        await save.Should().ThrowAsync<Exception>();
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task should_announce_a_stored_save_when_a_later_step_of_the_save_throws(int rows)
    {
        // given - one row is saved by a single command, two inside a transaction EF Core opens
        var afterSave = new AfterSaveThrows();
        await using var host = await LiveEfTestHost.CreateAsync(options => options.AddInterceptors(afterSave));
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.AddRange(Enumerable.Range(1, rows).Select(id => new Todo { Id = id, Title = $"todo {id}" }));
        afterSave.Armed = true;

        // when
        var save = () => db.SaveChangesAsync(AbortToken);

        // then - the save reported a failure and its rows are stored all the same
        await save.Should().ThrowAsync<InvalidOperationException>();
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(rows);
    }

    [Fact]
    public async Task should_announce_a_save_once_when_an_execution_strategy_runs_it_twice()
    {
        // given - the second insert fails once with a fault the strategy retries
        var failure = new SecondSaveCommandFailsOnce();
        await using var host = await LiveEfTestHost.CreateAsync(
            options => options.AddInterceptors(failure),
            sqlite => sqlite.ExecutionStrategy(dependencies => new RetryOnceExecutionStrategy(dependencies))
        );
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);

        // when
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.AddRange(new Todo { Id = 1, Title = "first" }, new Todo { Id = 2, Title = "second" });
        failure.Armed = true;
        await db.SaveChangesAsync(AbortToken);

        // then - the first try rolled back and announced nothing; the second announced the table once
        failure.Failures.Should().Be(1);
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(2);
    }

    [Fact]
    public async Task should_save_and_still_announce_the_table_when_reading_the_change_tracker_fails()
    {
        // given - a tracked todo whose next property read throws, which is what the tracker's own reading hits
        await using var host = await LiveEfTestHost.CreateAsync();
        await _SeedTodoAsync(host, id: 1, title: "old");
        await using var titles = host.Watch(
            (db, cancellationToken) => db.Todos.Select(todo => todo.Title).ToListAsync(cancellationToken),
            AbortToken
        );
        (await titles.NextAsync()).Should().Equal("old");

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var todo = await db.Todos.SingleAsync(AbortToken);
        todo.Title = "new";
        todo.FailNextTitleRead = true;

        // when
        var saved = await db.SaveChangesAsync(AbortToken);

        // then - the application's save went through, the failure was counted, and the watch still re-ran
        saved.Should().Be(1);
        host.Fallbacks.Should().Be(1);
        host.Wakes.Should().Be(1);
        (await titles.NextAsync()).Should().Equal("new");
    }

    [Fact]
    public async Task should_announce_every_table_of_an_entity_stored_in_two_tables()
    {
        // given - one watch per table of the split entity
        await using var host = await LiveEfTestHost.CreateAsync();

        await using (var seed = host.CreateScope())
        {
            var seedDb = seed.ServiceProvider.GetRequiredService<LiveDb>();
            seedDb.Profiles.Add(
                new Profile
                {
                    Id = 1,
                    Name = "Ada",
                    Bio = "Mathematician",
                }
            );
            await seedDb.SaveChangesAsync(AbortToken);
        }

        await using var names = host.Watch(
            (db, cancellationToken) =>
                db
                    .Database.SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM "Profiles" """)
                    .SingleAsync(cancellationToken),
            AbortToken
        );
        await using var details = host.Watch(
            (db, cancellationToken) =>
                db
                    .Database.SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM "ProfileDetails" """)
                    .SingleAsync(cancellationToken),
            AbortToken
        );
        (await names.NextAsync()).Should().Be(1);
        (await details.NextAsync()).Should().Be(1);

        // when - the save changes a column of the first table only
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var profile = await db.Profiles.SingleAsync(AbortToken);
        profile.Name = "Ada Lovelace";
        await db.SaveChangesAsync(AbortToken);

        // then - both watches were signalled, because a query may read either table for the entity
        host.Wakes.Should().Be(2);
    }

    [Fact]
    public async Task should_keep_finding_tables_when_more_command_texts_are_seen_than_it_remembers()
    {
        // given - a memory of one command text and two watches whose queries differ
        await using var host = await LiveEfTestHost.CreateAsync(configureTracking: options =>
            options.MaxCachedCommands = 1
        );
        await using var todos = host.WatchTodoCount(AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);
        (await counters.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();

        // when
        db.Todos.Add(new Todo { Id = 1, Title = "first" });
        await db.SaveChangesAsync(AbortToken);
        (await todos.NextAsync()).Should().Be(1);
        db.Counters.Add(new Counter { Id = 1, Value = 1 });
        await db.SaveChangesAsync(AbortToken);
        (await counters.NextAsync()).Should().Be(1);
        await db.Todos.ExecuteDeleteAsync(AbortToken);

        // then - each write signalled exactly the watch that read its table
        (await todos.NextAsync())
            .Should()
            .Be(0);
        host.Wakes.Should().Be(3);
    }

    private static async Task _SeedTodoAsync(LiveEfTestHost host, int id, string title)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        db.Todos.Add(new Todo { Id = id, Title = title });
        await db.SaveChangesAsync(AbortToken);
    }
}
