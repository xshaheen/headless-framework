// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;

namespace Tests;

/// <summary>
/// <c>RunAsync(db, …, retry, …)</c> for a block that reads a row, decides, and saves it under an optimistic
/// concurrency token: a save that lost the check replays the block on fresh rows when the call opted in, and never
/// when a replay could apply the block twice.
/// </summary>
/// <remarks>
/// SQLite takes the write lock when a transaction begins, so two writers never read the same row version by
/// accident. The contended scenarios therefore open every context on its own connection to one shared-cache
/// database and begin deferred, read-uncommitted transactions, which read without a lock, and hold every writer
/// after its first read until all of them have read. The single-writer scenarios lose the check to a rival write
/// issued between the block's read and its save.
/// </remarks>
public sealed class EfUnitOfWorkConflictReplayTests : TestBase
{
    private const int _Writers = 10;

    [Fact]
    public async Task should_apply_every_increment_when_ten_opted_in_blocks_contend_for_one_row()
    {
        await using var database = await ContendedDatabase.CreateAsync(AbortToken);

        var (faults, blockRuns) = await _RunContendedIncrementsAsync(
            database,
            EntityFrameworkConflictRetry.Create(maxAttempts: _Writers)
        );

        faults.Should().BeEmpty("a block that lost the check runs again instead of failing its caller");
        blockRuns
            .Should()
            .BeGreaterThanOrEqualTo(
                (2 * _Writers) - 1,
                "all ten read the same version, so nine of them lost their first save"
            );
        (await database.ReadCounterAsync(AbortToken)).Should().Be(_Writers);
    }

    [Fact]
    public async Task should_fail_the_losing_blocks_when_the_contending_blocks_did_not_opt_in()
    {
        await using var database = await ContendedDatabase.CreateAsync(AbortToken);

        var (faults, blockRuns) = await _RunContendedIncrementsAsync(database, retry: null);

        faults.Should().HaveCount(_Writers - 1, "only the first save matched the version every block read");
        blockRuns.Should().Be(_Writers, "nothing replays without the opt-in");
        (await database.ReadCounterAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_clear_the_tracker_and_read_the_row_again_when_the_block_replays()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        var trackedAtStart = new List<int>();
        var countersRead = new List<int>();

        await session.Factory.RunAsync(
            session.Db,
            async (_, ct) =>
            {
                trackedAtStart.Add(session.Db.ChangeTracker.Entries().Count());
                var row = await session.Db.Counters.SingleAsync(x => x.Id == id, ct);
                countersRead.Add(row.Counter);

                if (countersRead.Count == 1)
                {
                    await _RivalWriteAsync(session.Db, id, ct);
                    row.Counter = 999;
                }
                else
                {
                    row.Counter++;
                }

                row.Version++;
                await session.Db.SaveChangesAsync(ct);
            },
            IsolationLevel.ReadCommitted,
            EntityFrameworkConflictRetry.Create(),
            AbortToken
        );

        trackedAtStart.Should().Equal([0, 0], "a replay must not run over the entities the lost attempt edited");
        countersRead.Should().Equal([5, 5], "the replay reads the stored row, not the lost attempt's edit");
        (await _ReadCounterAsync(host, id)).Should().Be(6);
    }

    [Fact]
    public async Task should_surface_the_conflict_when_the_block_still_conflicts_on_its_last_attempt()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        var blockRuns = 0;

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                (_, ct) =>
                {
                    blockRuns++;

                    return _LoseTheCheckAsync(session.Db, id, ct);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(maxAttempts: 3),
                AbortToken
            );

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        blockRuns.Should().Be(3);
        (await _ReadCounterAsync(host, id)).Should().Be(5, "every attempt was rolled back");
    }

    [Fact]
    public async Task should_not_replay_and_should_report_in_doubt_when_the_commit_outcome_is_unknown()
    {
        var commitAnswerLost = new CommitAnswerLostInterceptor();
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureServices: services =>
            services.AddSingleton<IInterceptor>(commitAnswerLost)
        );
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        commitAnswerLost.Armed = true;
        var blockRuns = 0;

        // A policy that would replay any fault, so only the commit rule can keep the block from running twice.
        var replayEverything = new RetryStrategyOptions
        {
            ShouldHandle = static args => ValueTask.FromResult(args.Outcome.Exception is not null),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.Zero,
        };

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (_, ct) =>
                {
                    blockRuns++;
                    var row = await session.Db.Counters.SingleAsync(x => x.Id == id, ct);
                    row.Counter++;
                    row.Version++;
                    await session.Db.SaveChangesAsync(ct);
                },
                IsolationLevel.ReadCommitted,
                replayEverything,
                AbortToken
            );

        await act.Should().ThrowAsync<UnitOfWorkInDoubtException>();
        blockRuns.Should().Be(1);
        (await _ReadCounterAsync(host, id)).Should().Be(6, "the increment committed once before the answer was lost");
    }

    [Fact]
    public async Task should_not_replay_a_conflict_after_the_block_called_prevent_retry()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        var blockRuns = 0;

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                (unitOfWork, ct) =>
                {
                    blockRuns++;
                    unitOfWork.PreventRetry();

                    return _LoseTheCheckAsync(session.Db, id, ct);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        blockRuns.Should().Be(1);
    }

    [Fact]
    public async Task should_not_replay_a_conflict_after_the_block_ended_its_own_unit()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        var blockRuns = 0;

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (unitOfWork, ct) =>
                {
                    blockRuns++;

                    try
                    {
                        await _LoseTheCheckAsync(session.Db, id, ct);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        await unitOfWork.RollbackAsync();

                        throw;
                    }
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        blockRuns.Should().Be(1, "the block's own verb decided the unit's outcome");
    }

    [Fact]
    public async Task should_hand_the_conflict_to_the_owner_when_the_block_joined_a_live_unit()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        await using var owner = await session.Factory.BeginAsync(session.Db, AbortToken);
        var blockRuns = 0;

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                (_, ct) =>
                {
                    blockRuns++;

                    return _LoseTheCheckAsync(session.Db, id, ct);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        blockRuns.Should().Be(1, "replaying a joined block would run it again inside the owner's open transaction");
        owner.State.Should().Be(UnitOfWorkState.Active, "the owner decides what the conflict means for its unit");
    }

    [Fact]
    public async Task should_stop_replaying_when_the_token_is_cancelled()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var blockRuns = 0;

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (_, ct) =>
                {
                    blockRuns++;

                    try
                    {
                        await _LoseTheCheckAsync(session.Db, id, ct);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        await cancellation.CancelAsync();

                        throw;
                    }
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(maxAttempts: 5),
                cancellation.Token
            );

        await act.Should().ThrowAsync<Exception>();
        blockRuns.Should().Be(1, "a cancelled caller no longer wants the block's result");
        (await _ReadCounterAsync(host, id)).Should().Be(5);
    }

    [Fact]
    public async Task should_not_replay_a_conflict_when_the_context_tracked_entities_before_the_block()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        var row = await session.Db.Counters.SingleAsync(x => x.Id == id, AbortToken);
        var blockRuns = 0;

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (_, ct) =>
                {
                    blockRuns++;
                    await _RivalWriteAsync(session.Db, id, ct);
                    row.Counter++;
                    row.Version++;
                    await session.Db.SaveChangesAsync(ct);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        blockRuns.Should().Be(1, "a replay cannot restore entities the caller tracked before the block");
    }

    [Fact]
    public async Task should_not_replay_a_conflict_after_a_sibling_context_saved_an_edit_into_the_unit()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        var stockId = await _SeedProbeAsync(host, counter: 10);
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        var blockRuns = 0;

        var act = () =>
            owner.Factory.RunAsync(
                owner.Db,
                async (_, ct) =>
                {
                    blockRuns++;

                    // A relative update through a second context over the owner's connection: the save succeeds
                    // and the sibling keeps the edited row tracked after the unit rolls back.
                    await sibling.Factory.RunAsync(
                        sibling.Db,
                        async (_, inner) =>
                        {
                            var stock = await sibling.Db.Probes.SingleAsync(x => x.Id == stockId, inner);
                            stock.Counter += 3;
                            await sibling.Db.SaveChangesAsync(inner);
                        },
                        cancellationToken: ct
                    );

                    if (blockRuns == 1)
                    {
                        await _LoseTheCheckAsync(owner.Db, id, ct);
                    }
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        blockRuns.Should().Be(1, "a replay would add to the value the sibling context still holds in memory");
        (await _ReadProbeCounterAsync(host, stockId)).Should().Be(10, "the only attempt was rolled back");
    }

    [Fact]
    public async Task should_replay_and_save_both_attempts_rows_when_a_sibling_context_tracked_them_before_it_joined()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        var blockRuns = 0;

        await owner.Factory.RunAsync(
            owner.Db,
            async (_, ct) =>
            {
                blockRuns++;

                // Tracked before the sibling's first lookup of the unit: the unit learns of a second context only
                // when that context joins it, so nothing tells the replay decision about this row.
                sibling.Db.Probes.Add(new ProbeRow { Name = "audit" });

                if (blockRuns == 1)
                {
                    await _LoseTheCheckAsync(owner.Db, id, ct);
                }

                await sibling.Factory.RunAsync(
                    sibling.Db,
                    (_, inner) => sibling.Db.SaveChangesAsync(inner),
                    cancellationToken: ct
                );
            },
            IsolationLevel.ReadCommitted,
            EntityFrameworkConflictRetry.Create(),
            AbortToken
        );

        // The block meant one row. The failed attempt's row is still in the sibling's tracker when the replay adds
        // its own, and the sibling's save writes both. A block avoids this by tracking through a sibling only
        // after the sibling joined the unit, or by keeping sibling changes out of a replayable block.
        blockRuns.Should().Be(2, "the unit cannot see a context that has not joined it, so it does not refuse");
        (await host.CountProbeRowsAsync()).Should().Be(2, "the sibling still tracked the failed attempt's row");
    }

    [Fact]
    public async Task should_replay_a_conflict_when_the_sibling_context_tracks_nothing()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        var id = await _SeedCounterAsync(host, counter: 5);
        var stockId = await _SeedProbeAsync(host, counter: 10);
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        var blockRuns = 0;

        await owner.Factory.RunAsync(
            owner.Db,
            async (_, ct) =>
            {
                blockRuns++;

                // A set-based update leaves nothing in the sibling's tracker, so the rollback undoes all of it.
                await sibling.Factory.RunAsync(
                    sibling.Db,
                    (_, inner) =>
                        sibling
                            .Db.Probes.Where(x => x.Id == stockId)
                            .ExecuteUpdateAsync(
                                setters => setters.SetProperty(x => x.Counter, x => x.Counter + 3),
                                inner
                            ),
                    cancellationToken: ct
                );

                if (blockRuns == 1)
                {
                    await _LoseTheCheckAsync(owner.Db, id, ct);
                }
            },
            IsolationLevel.ReadCommitted,
            EntityFrameworkConflictRetry.Create(),
            AbortToken
        );

        blockRuns.Should().Be(2);
        (await _ReadProbeCounterAsync(host, stockId)).Should().Be(13, "the update was applied once");
    }

    [Fact]
    public async Task should_add_the_conflict_replays_to_the_execution_strategy_retries_without_multiplying_them()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        var id = await _SeedCounterAsync(host, counter: 5);
        await using var session = host.CreateSession();
        var blockRuns = 0;

        // One conflict replay and one strategy retry allow three runs. The second run's transient fault restarts
        // the strategy's attempt, which must not hand the conflict policy a second replay.
        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                (_, ct) =>
                    ++blockRuns == 2
                        ? Task.FromException(new TransientMarkerException())
                        : _LoseTheCheckAsync(session.Db, id, ct),
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(maxAttempts: 2),
                AbortToken
            );

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        blockRuns.Should().Be(3);
    }

    [Fact]
    public void should_reject_an_attempt_cap_that_allows_no_replay()
    {
        var act = () => EntityFrameworkConflictRetry.Create(maxAttempts: 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Runs ten blocks at once that each read the counter, wait until every block has read it, and save an
    /// increment, and returns the conflicts that reached a caller and how many times a block ran.
    /// </summary>
    private static async Task<(
        IReadOnlyList<DbUpdateConcurrencyException> Faults,
        int BlockRuns
    )> _RunContendedIncrementsAsync(ContendedDatabase database, RetryStrategyOptions? retry)
    {
        var everyWriterRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writersThatRead = 0;
        var blockRuns = 0;

        var writers = Enumerable
            .Range(0, _Writers)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        await using var scope = database.Root.CreateAsyncScope();
                        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
                        var firstRun = true;

                        try
                        {
                            await factory.RunAsync(
                                db,
                                async (_, ct) =>
                                {
                                    Interlocked.Increment(ref blockRuns);
                                    var row = await db.Counters.SingleAsync(x => x.Id == database.CounterId, ct);

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

    /// <summary>Reads the counter, lets a rival change it, and saves an increment that therefore loses the check.</summary>
    private static async Task _LoseTheCheckAsync(ProbeDbContext db, int id, CancellationToken cancellationToken)
    {
        var row = await db.Counters.SingleAsync(x => x.Id == id, cancellationToken);
        await _RivalWriteAsync(db, id, cancellationToken);
        row.Counter++;
        row.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Stands in for another writer: moves the stored version past the one the block read, without touching the
    /// block's tracked entity. It runs in the block's transaction, so the rollback of a lost attempt undoes it.
    /// </summary>
    private static Task _RivalWriteAsync(ProbeDbContext db, int id, CancellationToken cancellationToken)
    {
        return db
            .Counters.Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.Version, x => x.Version + 100),
                cancellationToken
            );
    }

    private static async Task<int> _SeedCounterAsync(EfUnitOfWorkHost host, int counter)
    {
        await using var scope = host.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var row = new VersionedRow { Counter = counter };
        db.Counters.Add(row);
        await db.SaveChangesAsync(AbortToken);

        return row.Id;
    }

    private static async Task<int> _SeedProbeAsync(EfUnitOfWorkHost host, int counter)
    {
        await using var scope = host.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var row = new ProbeRow { Name = "stock", Counter = counter };
        db.Probes.Add(row);
        await db.SaveChangesAsync(AbortToken);

        return row.Id;
    }

    private static async Task<int> _ReadProbeCounterAsync(EfUnitOfWorkHost host, int id)
    {
        await using var scope = host.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        return await db.Probes.AsNoTracking().Where(x => x.Id == id).Select(x => x.Counter).SingleAsync(AbortToken);
    }

    private static async Task<int> _ReadCounterAsync(EfUnitOfWorkHost host, int id)
    {
        await using var scope = host.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        return await db.Counters.AsNoTracking().Where(x => x.Id == id).Select(x => x.Counter).SingleAsync(AbortToken);
    }

    /// <summary>
    /// One shared-cache in-memory database that every context opens its own connection to, so ten units of work
    /// hold ten transactions at once. The kept connection keeps the database alive between contexts.
    /// </summary>
    private sealed class ContendedDatabase(SqliteConnection keepAlive, ServiceProvider root, int counterId)
        : IAsyncDisposable
    {
        public ServiceProvider Root { get; } = root;

        public int CounterId { get; } = counterId;

        public static async Task<ContendedDatabase> CreateAsync(CancellationToken cancellationToken)
        {
            var connectionString = $"Data Source=conflict-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
#pragma warning disable CA2000 // False positive: the returned database owns the connection and disposes it.
            var keepAlive = new SqliteConnection(connectionString);
#pragma warning restore CA2000
            await keepAlive.OpenAsync(cancellationToken);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddUnitOfWork();
            services.AddEntityFrameworkUnitOfWork();
            services.AddDbContext<ProbeDbContext>(options =>
                options.UseSqlite(connectionString).AddInterceptors(new LockFreeReadTransactionInterceptor())
            );

            var root = services.BuildServiceProvider();

            await using var scope = root.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var row = new VersionedRow();
            db.Counters.Add(row);
            await db.SaveChangesAsync(cancellationToken);

            return new ContendedDatabase(keepAlive, root, row.Id);
        }

        public async Task<int> ReadCounterAsync(CancellationToken cancellationToken)
        {
            await using var scope = Root.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

            return await db
                .Counters.AsNoTracking()
                .Where(x => x.Id == CounterId)
                .Select(x => x.Counter)
                .SingleAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            await keepAlive.DisposeAsync();
        }
    }

    /// <summary>
    /// Begins every transaction deferred and read-uncommitted, the one SQLite mode in which a transaction reads a
    /// row without locking its table, so several open transactions can read one row version before any of them
    /// writes. EF Core's own begin takes the write lock immediately and would run the writers one after another.
    /// </summary>
    private sealed class LockFreeReadTransactionInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default
        )
        {
            var transaction = ((SqliteConnection)connection).BeginTransaction(
                IsolationLevel.ReadUncommitted,
                deferred: true
            );

            return ValueTask.FromResult(InterceptionResult<DbTransaction>.SuppressWithResult(transaction));
        }
    }

    /// <summary>
    /// Loses the commit's answer after the database committed: the caller cannot tell whether the transaction is
    /// durable.
    /// </summary>
    private sealed class CommitAnswerLostInterceptor : DbTransactionInterceptor
    {
        /// <summary>Off while the test seeds its row, so only the block's commit loses its answer.</summary>
        public bool Armed { get; set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            return Armed
                ? throw new TimeoutException("The connection timed out before the commit was acknowledged.")
                : Task.CompletedTask;
        }
    }
}
