// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

/// <summary>
/// Proves on each engine that a replayable <c>RunAsync(owner, …)</c> block refuses its replay when a second
/// <see cref="HeadlessDbContext" /> over the unit's connection tracked a row before it joined the unit, instead of
/// replaying and saving the failed attempt's row a second time. The block's own rows keep it replayable.
/// </summary>
public abstract class SiblingContextReplayTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : ISiblingContextReplayFixture
{
    [Fact]
    public async Task should_refuse_the_replay_when_a_sibling_headless_context_tracked_a_row_before_it_joined()
    {
        // given
        await using var session = await Session.CreateAsync(fixture);
        var owner = session.CreateContext();
        var sibling = session.CreateContext();
        var conflict = new DbUpdateConcurrencyException("Simulated optimistic concurrency fault.");
        var blockRuns = 0;

        // when
        var act = async () =>
            await session.Factory.RunAsync(
                owner,
                async (_, ct) =>
                {
                    blockRuns++;

                    // Tracked before the sibling ever looks the unit up: no join, no transaction adopted yet.
                    sibling.Rows.Add(new SiblingReplayRow { Id = Guid.CreateVersion7(), Name = session.Marker });

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
        (await session.CountRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_replay_and_save_one_row_when_only_the_block_context_tracked_it()
    {
        // given
        await using var session = await Session.CreateAsync(fixture);
        var owner = session.CreateContext();
        var blockRuns = 0;

        // when
        await session.Factory.RunAsync(
            owner,
            async (_, ct) =>
            {
                blockRuns++;
                owner.Rows.Add(new SiblingReplayRow { Id = Guid.CreateVersion7(), Name = session.Marker });

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
        (await session.CountRowsAsync()).Should().Be(1);
    }

    /// <summary>A service provider whose contexts all run on one open connection, as a consumer shares one with a unit.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly List<AsyncServiceScope> _scopes = [];
        private DbConnection _connection = null!;
        private ServiceProvider _provider = null!;

        public string Marker { get; } = Guid.NewGuid().ToString("N");

        public IUnitOfWorkFactory Factory => _provider.GetRequiredService<IUnitOfWorkFactory>();

        public static async Task<Session> CreateAsync(TFixture fixture)
        {
            var session = new Session { _connection = fixture.CreateConnection() };
            await session._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHeadlessDbContext<SiblingReplayDbContext>(options =>
                fixture.UseConnection(options, session._connection)
            );
            session._provider = services.BuildServiceProvider();

            // The fixture's database starts empty; a later session finds the table and creates nothing.
            await session.CreateContext().Database.EnsureCreatedAsync(AbortToken);

            return session;
        }

        /// <summary>A context from its own scope, so two calls give two instances over the shared connection.</summary>
        public SiblingReplayDbContext CreateContext()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope.ServiceProvider.GetRequiredService<SiblingReplayDbContext>();
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

public interface ISiblingContextReplayFixture
{
    DbConnection CreateConnection();

    void UseConnection(DbContextOptionsBuilder options, DbConnection connection);
}

public sealed class SiblingReplayRow : Entity<Guid>
{
    public required string Name { get; init; }
}

public sealed class SiblingReplayDbContext(DbContextOptions<SiblingReplayDbContext> options)
    : HeadlessDbContext(options)
{
    public DbSet<SiblingReplayRow> Rows => Set<SiblingReplayRow>();

    public override string? DefaultSchema => null;
}

[UsedImplicitly]
public sealed class PostgreSqlSiblingContextReplayFixture : HeadlessPostgreSqlFixture, ISiblingContextReplayFixture
{
    public DbConnection CreateConnection() => new NpgsqlConnection(ConnectionString);

    public void UseConnection(DbContextOptionsBuilder options, DbConnection connection) =>
        options.UseNpgsql(connection);
}

[UsedImplicitly]
public sealed class SqlServerSiblingContextReplayFixture : HeadlessSqlServerFixture, ISiblingContextReplayFixture
{
    public DbConnection CreateConnection() => new SqlConnection(ConnectionString);

    public void UseConnection(DbContextOptionsBuilder options, DbConnection connection) =>
        options.UseSqlServer(connection);
}

public sealed class PostgreSqlSiblingContextReplayTests(PostgreSqlSiblingContextReplayFixture fixture)
    : SiblingContextReplayTests<PostgreSqlSiblingContextReplayFixture>(fixture),
        IClassFixture<PostgreSqlSiblingContextReplayFixture>;

public sealed class SqlServerSiblingContextReplayTests(SqlServerSiblingContextReplayFixture fixture)
    : SiblingContextReplayTests<SqlServerSiblingContextReplayFixture>(fixture),
        IClassFixture<SqlServerSiblingContextReplayFixture>;
