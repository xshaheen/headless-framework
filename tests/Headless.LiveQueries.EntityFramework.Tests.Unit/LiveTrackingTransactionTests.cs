// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Transactions;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveTrackingTransactionTests : TestBase
{
    [Fact]
    public async Task should_announce_once_after_the_commit_when_a_transaction_holds_two_saves()
    {
        // given - one watch that reads both tables the transaction writes
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var rows = host.Watch(
            async (db, cancellationToken) =>
                await db.Todos.CountAsync(cancellationToken) + await db.Counters.CountAsync(cancellationToken),
            AbortToken
        );
        (await rows.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        // when
        db.Todos.Add(new Todo { Id = 1, Title = "first save" });
        await db.SaveChangesAsync(AbortToken);
        db.Counters.Add(new Counter { Id = 1, Value = 1 });
        await db.SaveChangesAsync(AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await transaction.CommitAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await rows.NextAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_announce_nothing_for_a_transaction_that_is_rolled_back_or_disposed_without_a_commit(
        bool explicitRollback
    )
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
        await using (var transaction = await db.Database.BeginTransactionAsync(AbortToken))
        {
            db.Todos.Add(new Todo { Id = 1, Title = "never committed" });
            await db.SaveChangesAsync(AbortToken);

            if (explicitRollback)
            {
                await transaction.RollbackAsync(AbortToken);
            }
        }

        // then
        host.Wakes.Should().Be(0);

        // and the same context's next write announces its own table only
        db.ChangeTracker.Clear();
        db.Counters.Add(new Counter { Id = 1, Value = 1 });
        await db.SaveChangesAsync(AbortToken);
        host.Wakes.Should().Be(1);
        (await counters.NextAsync()).Should().Be(1);
        todos.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_announce_the_written_tables_when_a_transactions_commit_throws()
    {
        // given - a commit the database applies and whose answer never arrives
        var answerLost = new CommitAnswerLostInterceptor();
        await using var host = await LiveEfTestHost.CreateAsync(options => options.AddInterceptors(answerLost));
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        db.Todos.Add(new Todo { Id = 1, Title = "maybe stored" });
        await db.SaveChangesAsync(AbortToken);
        answerLost.Armed = true;

        // when
        var commit = () => transaction.CommitAsync(AbortToken);

        // then
        await commit.Should().ThrowAsync<TimeoutException>();
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_announce_a_second_contexts_writes_when_the_context_that_began_the_transaction_commits()
    {
        // given
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var notes = host.WatchNotes(
            (db, cancellationToken) => db.Notes.CountAsync(cancellationToken),
            AbortToken
        );
        (await notes.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var owner = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var joiner = scope.ServiceProvider.GetRequiredService<NotesDb>();
        await using var transaction = await owner.Database.BeginTransactionAsync(AbortToken);
        await joiner.Database.UseTransactionAsync(transaction.GetDbTransaction(), AbortToken);

        // when
        joiner.Notes.Add(new Note { Id = 1, Text = "from the second context" });
        await joiner.SaveChangesAsync(AbortToken);
        await joiner.Database.UseTransactionAsync(transaction: null, AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await transaction.CommitAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await notes.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_announce_a_save_inside_a_transaction_scope_only_after_the_scope_completes()
    {
        // given - SQLite cannot enlist in the scope and EF Core refuses the save unless told to go on, so this shows
        // only when the tracker announces; the integration tests show it against providers that do enlist
        await using var host = await LiveEfTestHost.CreateAsync(options =>
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.AmbientTransactionWarning))
        );
        await using var todos = host.WatchTodoCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);
        long wakesInsideScope;

        // when
        using (var transactionScope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var scope = host.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
            db.Todos.Add(new Todo { Id = 1, Title = "inside the scope" });
            await db.SaveChangesAsync(AbortToken);
            wakesInsideScope = host.Wakes;
            transactionScope.Complete();
        }

        // then
        wakesInsideScope.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await todos.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_carry_nothing_from_one_lease_of_a_pooled_context_to_the_next()
    {
        // given - a pool of one context, so the second request gets the instance the first one used
        await using var host = await LiveEfTestHost.CreateAsync(pooled: true);
        await using var todos = host.WatchTodoCount(AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await todos.NextAsync()).Should().Be(0);
        (await counters.NextAsync()).Should().Be(0);
        LiveDb first;

        // when - the first request saves inside a transaction it never commits
        await using (var request = host.CreateScope())
        {
            first = request.ServiceProvider.GetRequiredService<LiveDb>();
            await using var abandoned = await first.Database.BeginTransactionAsync(AbortToken);
            first.Todos.Add(new Todo { Id = 1, Title = "never committed" });
            await first.SaveChangesAsync(AbortToken);
        }

        // and the second request commits a write to another table
        await using (var request = host.CreateScope())
        {
            var second = request.ServiceProvider.GetRequiredService<LiveDb>();
            second.Should().BeSameAs(first, "the scenario needs the pool to hand the same instance out again");
            await using var transaction = await second.Database.BeginTransactionAsync(AbortToken);
            second.Counters.Add(new Counter { Id = 1, Value = 1 });
            await second.SaveChangesAsync(AbortToken);
            await transaction.CommitAsync(AbortToken);
        }

        // then - only the second request's table was announced
        host.Wakes.Should().Be(1);
        (await counters.NextAsync()).Should().Be(1);
        todos.Runs.Should().Be(1);
    }
}
