// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;

namespace Tests;

/// <summary>
/// <c>RunAsync(db, …, retry, …)</c> against a real relational engine: a save that lost the optimistic concurrency
/// check replays the block on the stored row when the call opted in through
/// <see cref="EntityFrameworkConflictRetry" />, and fails the caller when it did not. The engine's own row locking
/// decides which save wins, so each provider shows that its lost check reaches EF Core as
/// <see cref="DbUpdateConcurrencyException" />.
/// </summary>
public abstract class EntityFrameworkConflictReplayConformanceTests(EntityFrameworkConflictReplayFixture fixture)
    : TestBase
{
    private const int _Writers = 10;

    [Fact]
    public virtual async Task should_apply_every_increment_when_ten_opted_in_blocks_contend_for_one_row()
    {
        var id = await fixture.SeedCounterAsync(counter: 0, AbortToken);

        var (faults, blockRuns) = await _RunContendedIncrementsAsync(
            id,
            EntityFrameworkConflictRetry.Create(maxAttempts: _Writers)
        );

        faults.Should().BeEmpty("a block that lost the check runs again instead of failing its caller");
        blockRuns
            .Should()
            .BeGreaterThanOrEqualTo(
                (2 * _Writers) - 1,
                "all ten read the same version, so nine of them lost their first save"
            );
        (await fixture.ReadCounterAsync(id, AbortToken)).Should().Be(_Writers);
    }

    [Fact]
    public virtual async Task should_fail_the_losing_blocks_when_the_contending_blocks_did_not_opt_in()
    {
        var id = await fixture.SeedCounterAsync(counter: 0, AbortToken);

        var (faults, blockRuns) = await _RunContendedIncrementsAsync(id, retry: null);

        faults.Should().HaveCount(_Writers - 1, "only the first save matched the version every block read");
        blockRuns.Should().Be(_Writers, "nothing replays without the opt-in");
        (await fixture.ReadCounterAsync(id, AbortToken)).Should().Be(1);
    }

    [Fact]
    public virtual async Task should_surface_the_conflict_when_the_block_still_conflicts_on_its_last_attempt()
    {
        var id = await fixture.SeedCounterAsync(counter: 5, AbortToken);
        await using var provider = fixture.BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
        var db = scope.ServiceProvider.GetRequiredService<ConflictCounterDbContext>();
        var blockRuns = 0;

        DbUpdateConcurrencyException? conflict = null;

        // Awaited in place rather than through a deferred delegate, so the scope's context outlives every attempt.
        try
        {
            await factory.RunAsync(
                db,
                async (_, ct) =>
                {
                    blockRuns++;
                    await _LoseTheCheckAsync(db, id, ct);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(maxAttempts: 3),
                AbortToken
            );
        }
        catch (DbUpdateConcurrencyException e)
        {
            conflict = e;
        }

        conflict.Should().NotBeNull("the last attempt's lost check reaches the caller");
        blockRuns.Should().Be(3);
        (await fixture.ReadCounterAsync(id, AbortToken)).Should().Be(5, "every attempt was rolled back");
    }

    /// <summary>
    /// Runs ten blocks at once, each on its own scope and connection, that read the counter, wait until every block
    /// has read it, and save an increment. Returns the conflicts that reached a caller and how many times a block ran.
    /// </summary>
    private async Task<(
        IReadOnlyList<DbUpdateConcurrencyException> Faults,
        int BlockRuns
    )> _RunContendedIncrementsAsync(int id, RetryStrategyOptions? retry)
    {
        await using var provider = fixture.BuildProvider();
        var everyWriterRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writersThatRead = 0;
        var blockRuns = 0;

        var writers = Enumerable
            .Range(0, _Writers)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        await using var scope = provider.CreateAsyncScope();
                        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                        var db = scope.ServiceProvider.GetRequiredService<ConflictCounterDbContext>();
                        var firstRun = true;

                        try
                        {
                            await factory.RunAsync(
                                db,
                                async (_, ct) =>
                                {
                                    Interlocked.Increment(ref blockRuns);
                                    var row = await db.Counters.SingleAsync(x => x.Id == id, ct);

                                    if (firstRun)
                                    {
                                        firstRun = false;

                                        if (Interlocked.Increment(ref writersThatRead) == _Writers)
                                        {
                                            everyWriterRead.SetResult();
                                        }

                                        await everyWriterRead.Task.WaitAsync(ct);
                                    }

                                    row.Counter++;
                                    row.Version++;
                                    await db.SaveChangesAsync(ct);
                                },
                                IsolationLevel.ReadCommitted,
                                retry,
                                AbortToken
                            );

                            return null;
                        }
                        catch (DbUpdateConcurrencyException ex)
                        {
                            return ex;
                        }
                    },
                    AbortToken
                )
            );

        var outcomes = await Task.WhenAll(writers);

        return (outcomes.OfType<DbUpdateConcurrencyException>().ToArray(), blockRuns);
    }

    /// <summary>
    /// Reads the counter, then moves the stored version past the one it read inside the block's own transaction, so
    /// the save that follows loses the check and the attempt's rollback undoes the rival write.
    /// </summary>
    private static async Task _LoseTheCheckAsync(
        ConflictCounterDbContext db,
        int id,
        CancellationToken cancellationToken
    )
    {
        var row = await db.Counters.SingleAsync(x => x.Id == id, cancellationToken);
        await db
            .Counters.Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.Version, x => x.Version + 100),
                cancellationToken
            );
        row.Counter++;
        row.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }
}
