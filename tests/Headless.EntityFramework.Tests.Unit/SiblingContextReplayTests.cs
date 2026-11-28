// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A replayable <c>RunAsync(owner, …)</c> block that tracks rows through a second <see cref="HeadlessDbContext" />
/// over the unit's connection: the sibling never joined the unit when the attempt failed, so a replay would run
/// over the rows the failed attempt left in its tracker and save them twice. The sibling's change tracker makes the
/// unit aware of it, and the unit refuses the replay instead.
/// </summary>
public sealed class SiblingContextReplayTests : TestBase
{
    [Fact]
    public async Task should_refuse_the_replay_when_a_sibling_headless_context_tracked_a_row_before_it_joined()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        var owner = harness.CreateContext();
        var sibling = harness.CreateContext();
        var conflict = new DbUpdateConcurrencyException("Simulated optimistic concurrency fault.");
        var blockRuns = 0;

        // when
        var act = async () =>
            await harness.Factory.RunAsync(
                owner,
                async (_, ct) =>
                {
                    blockRuns++;

                    // Tracked before the sibling ever looks the unit up: no join, no transaction adopted yet.
                    sibling.Rows.Add(new ReplayRow { Id = Guid.CreateVersion7(), Name = harness.Marker });

                    if (blockRuns == 1)
                    {
                        throw conflict;
                    }

                    await sibling.SaveChangesAsync(ct);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        // then
        (await act.Should().ThrowAsync<DbUpdateConcurrencyException>())
            .Which.Should()
            .BeSameAs(conflict);
        blockRuns.Should().Be(1, "a replay would save the failed attempt's row the sibling still tracks");
        sibling.Database.CurrentTransaction.Should().BeNull("registering for the replay decision adopts nothing");
        (await harness.CountRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_replay_when_only_the_block_context_tracked_rows()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        var owner = harness.CreateContext();
        var blockRuns = 0;

        // when
        await harness.Factory.RunAsync(
            owner,
            async (_, ct) =>
            {
                blockRuns++;
                owner.Rows.Add(new ReplayRow { Id = Guid.CreateVersion7(), Name = harness.Marker });

                if (blockRuns == 1)
                {
                    throw new DbUpdateConcurrencyException("Simulated optimistic concurrency fault.");
                }

                await owner.SaveChangesAsync(ct);
            },
            IsolationLevel.ReadCommitted,
            EntityFrameworkConflictRetry.Create(),
            AbortToken
        );

        // then: the guard clears the block's own tracker, so its own rows never stop a replay
        blockRuns.Should().Be(2);
        (await harness.CountRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_replay_when_the_sibling_only_read_without_tracking()
    {
        // given
        await using var harness = await Harness.CreateAsync();
        var owner = harness.CreateContext();
        var sibling = harness.CreateContext();
        var blockRuns = 0;

        // when
        await harness.Factory.RunAsync(
            owner,
            async (_, ct) =>
            {
                blockRuns++;
                await sibling.Rows.AsNoTracking().CountAsync(ct);
                owner.Rows.Add(new ReplayRow { Id = Guid.CreateVersion7(), Name = harness.Marker });

                if (blockRuns == 1)
                {
                    throw new DbUpdateConcurrencyException("Simulated optimistic concurrency fault.");
                }

                await owner.SaveChangesAsync(ct);
            },
            IsolationLevel.ReadCommitted,
            EntityFrameworkConflictRetry.Create(),
            AbortToken
        );

        // then
        blockRuns.Should().Be(2);
        (await harness.CountRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_refuse_the_replay_in_a_later_unit_when_the_sibling_served_an_earlier_one()
    {
        // given: the sibling tracked and saved inside a first unit that committed
        await using var harness = await Harness.CreateAsync();
        var owner = harness.CreateContext();
        var sibling = harness.CreateContext();

        await harness.Factory.RunAsync(
            owner,
            async (_, ct) =>
            {
                sibling.Rows.Add(new ReplayRow { Id = Guid.CreateVersion7(), Name = harness.Marker });
                await sibling.SaveChangesAsync(ct);
            },
            AbortToken
        );

        sibling.ChangeTracker.Clear();
        owner.ChangeTracker.Clear();
        var conflict = new DbUpdateConcurrencyException("Simulated optimistic concurrency fault.");
        var blockRuns = 0;

        // when: a second unit on the same connection, where the sibling tracks again before the fault
        var act = async () =>
            await harness.Factory.RunAsync(
                owner,
                (_, _) =>
                {
                    blockRuns++;
                    sibling.Rows.Add(new ReplayRow { Id = Guid.CreateVersion7(), Name = harness.Marker });

                    return Task.FromException(conflict);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        // then: the sibling is known to the new unit, not only to the one it served before
        (await act.Should().ThrowAsync<DbUpdateConcurrencyException>())
            .Which.Should()
            .BeSameAs(conflict);
        blockRuns.Should().Be(1);
        (await harness.CountRowsAsync()).Should().Be(1, "only the first unit committed");
    }

    public sealed class ReplayRow : Entity<Guid>
    {
        public required string Name { get; init; }
    }

    public sealed class ReplayDbContext(DbContextOptions<ReplayDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<ReplayRow> Rows => Set<ReplayRow>();

        public override string DefaultSchema => "";
    }

    /// <summary>One in-memory database on one open connection, which every context the harness creates shares.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];
        private ServiceProvider _provider = null!;

        public string Marker { get; } = Guid.NewGuid().ToString("N");

        public IUnitOfWorkFactory Factory => _provider.GetRequiredService<IUnitOfWorkFactory>();

        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHeadlessDbContext<ReplayDbContext>(options => options.UseSqlite(harness._connection));
            harness._provider = services.BuildServiceProvider();
            await harness.CreateContext().Database.EnsureCreatedAsync(AbortToken);

            return harness;
        }

        /// <summary>A context from its own scope, so two calls give two instances over the shared connection.</summary>
        public ReplayDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<ReplayDbContext>();
        }

        public Task<int> CountRowsAsync() =>
            CreateContext().Rows.AsNoTracking().CountAsync(x => x.Name == Marker, AbortToken);

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
            {
                await scope.DisposeAsync();
            }

            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
