// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveTrackingUnitOfWorkTests : TestBase
{
    [Fact]
    public async Task should_announce_nothing_when_a_unit_of_work_saves_and_rolls_back()
    {
        // given
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var todos = host.WatchTodoCount(AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);
        (await counters.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();

        // when
        await using (var unit = await host.UnitOfWork.BeginAsync(db, AbortToken))
        {
            db.Todos.Add(new Todo { Id = 1, Title = "rolled back" });
            await db.SaveChangesAsync(AbortToken);
            await unit.RollbackAsync();
        }

        // then
        host.Wakes.Should().Be(0);

        // and nothing of the rolled-back unit rides along with the context's next write
        db.ChangeTracker.Clear();
        db.Counters.Add(new Counter { Id = 1, Value = 1 });
        await db.SaveChangesAsync(AbortToken);
        host.Wakes.Should().Be(1);
        (await counters.NextAsync()).Should().Be(1);
        todos.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_announce_once_after_the_commit_when_a_unit_of_work_saves_twice()
    {
        // given - one watch that reads both tables the unit writes
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var rows = _WatchTodosPlusCounters(host);
        (await rows.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);

        // when
        db.Todos.Add(new Todo { Id = 1, Title = "first save" });
        await db.SaveChangesAsync(AbortToken);
        db.Counters.Add(new Counter { Id = 1, Value = 1 });
        await db.SaveChangesAsync(AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await unit.CompleteAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await rows.NextAsync()).Should().Be(2);
    }

    [Fact]
    public async Task should_announce_the_written_tables_when_the_commit_fails_with_an_unknown_outcome()
    {
        // given - a commit the database applies and whose answer never arrives
        var answerLost = new CommitAnswerLostInterceptor();
        await using var host = await LiveEfTestHost.CreateAsync(options => options.AddInterceptors(answerLost));
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);
        db.Todos.Add(new Todo { Id = 1, Title = "maybe stored" });
        await db.SaveChangesAsync(AbortToken);
        answerLost.Armed = true;

        // when
        var commit = async () => await unit.CompleteAsync(AbortToken);

        // then - the write may be stored, so the watch re-runs and finds it
        await commit.Should().ThrowAsync<UnitOfWorkInDoubtException>();
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_announce_nothing_when_the_commit_fails_before_it_reaches_the_database()
    {
        // given
        var refused = new CommitRefusedInterceptor();
        await using var host = await LiveEfTestHost.CreateAsync(options => options.AddInterceptors(refused));
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);
        db.Todos.Add(new Todo { Id = 1, Title = "not stored" });
        await db.SaveChangesAsync(AbortToken);
        refused.Armed = true;

        // when
        var commit = async () => await unit.CompleteAsync(AbortToken);

        // then
        await commit.Should().ThrowAsync<InvalidOperationException>();
        unit.Failure!.Reason.Should().Be(UnitOfWorkFailureReason.Faulted);
        host.Wakes.Should().Be(0);
    }

    [Fact]
    public async Task should_announce_a_contexts_writes_when_the_raw_ado_unit_it_ran_inside_commits()
    {
        // given - a unit begun on the connection itself, and a context that adopts its transaction
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var unit = await host.UnitOfWork.BeginAsync(host.Connection, AbortToken);
        var transaction = ((IRelationalUnitOfWorkResource)unit.Resource!).Transaction;
        await db.Database.UseTransactionAsync(transaction, AbortToken);

        // when
        db.Todos.Add(new Todo { Id = 1, Title = "inside the raw unit" });
        await db.SaveChangesAsync(AbortToken);
        await db.Database.UseTransactionAsync(transaction: null, AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await unit.CompleteAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_announce_a_second_contexts_writes_when_the_owner_of_the_unit_it_joined_commits()
    {
        // given - two context types over one connection, each watched
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var todos = host.WatchTodoCount(AbortToken);
        await using var notes = host.WatchNotes(
            (db, cancellationToken) => db.Notes.CountAsync(cancellationToken),
            AbortToken
        );
        (await todos.NextAsync()).Should().Be(0);
        (await notes.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var owner = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var joiner = scope.ServiceProvider.GetRequiredService<NotesDb>();
        await using var unit = await host.UnitOfWork.BeginAsync(owner, AbortToken);

        // when - the second context joins the unit, and only the owner's unit commits
        await host.UnitOfWork.RunAsync(
            joiner,
            async (_, cancellationToken) =>
            {
                joiner.Notes.Add(new Note { Id = 1, Text = "from the second context" });
                await joiner.SaveChangesAsync(cancellationToken);
            },
            cancellationToken: AbortToken
        );
        owner.Todos.Add(new Todo { Id = 1, Title = "from the owner" });
        await owner.SaveChangesAsync(AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await unit.CompleteAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(2);
        (await todos.NextAsync()).Should().Be(1);
        (await notes.NextAsync()).Should().Be(1);
    }

    private static Watch<int> _WatchTodosPlusCounters(LiveEfTestHost host)
    {
        return host.Watch(
            async (db, cancellationToken) =>
                await db.Todos.CountAsync(cancellationToken) + await db.Counters.CountAsync(cancellationToken),
            AbortToken
        );
    }
}
