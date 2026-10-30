// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA2025 // False positive: each assertion awaits the enlisted call before the unit it captured is disposed.

/// <summary>
/// A store's context from its own factory runs one call on a caller's unit-of-work transaction, then gets its own
/// connection back. Each test uses its own database file under the test output: an open in-memory SQLite connection
/// reports no data source, and an open file connection reports its resolved path, so a temp directory behind a symlink
/// (macOS) would not match the configured one.
/// </summary>
public sealed class EntityFrameworkEnlistmentTests : TestBase
{
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, $"enlist-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;

    private readonly ServiceProvider _services;
    private readonly SqliteConnection _unitConnection;

    public EntityFrameworkEnlistmentTests()
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();
        _services = services.BuildServiceProvider();
        _connectionString = $"Data Source={_databasePath};Pooling=False";
        _unitConnection = new SqliteConnection(_connectionString);
    }

    private IUnitOfWorkFactory Factory => _services.GetRequiredService<IUnitOfWorkFactory>();

    [Fact]
    public async Task should_write_inside_the_unit_and_roll_back_with_it()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var storeFactory = _PooledFactory(options => options.UseSqlite(_connectionString));

        // when
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            var seen = await EntityFrameworkEnlistment.RunAsync(
                storeFactory,
                unit,
                "the probe store",
                "probe write",
                async (db, ct) =>
                {
                    db.Probes.Add(new ProbeRow { Name = "enlisted" });
                    await db.SaveChangesAsync(ct);

                    return await db.Probes.CountAsync(ct);
                },
                AbortToken
            );

            seen.Should().Be(1);
            await unit.RollbackAsync();
        }

        // then
        (await _CountAsync())
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task should_commit_with_the_unit_and_give_the_pooled_context_its_connection_back()
    {
        // given one pooled instance, so the next lease is the context that borrowed the unit's connection
        await using var owner = await _CreateOwnerAsync();
        var storeFactory = _PooledFactory(options => options.UseSqlite(_connectionString));

        // when
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await EntityFrameworkEnlistment.RunAsync(
                storeFactory,
                unit,
                "the probe store",
                "probe write",
                (db, ct) =>
                {
                    db.Probes.Add(new ProbeRow { Name = "enlisted" });

                    return db.SaveChangesAsync(ct);
                },
                AbortToken
            );

            await unit.CompleteAsync(AbortToken);
        }

        // then
        await using var next = await storeFactory.CreateDbContextAsync(AbortToken);
        next.Database.GetDbConnection().Should().NotBeSameAs(_unitConnection);
        next.Database.GetConnectionString().Should().Be(_connectionString);
        next.Database.CurrentTransaction.Should().BeNull();
        (await next.Probes.CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_restore_a_connection_the_context_options_supply()
    {
        // given a store context configured with its own connection it does not own
        await using var owner = await _CreateOwnerAsync();
        await using var supplied = new SqliteConnection(_connectionString);
        var storeFactory = _PooledFactory(options => options.UseSqlite(supplied));

        // when
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await EntityFrameworkEnlistment.RunAsync(
                storeFactory,
                unit,
                "the probe store",
                "probe read",
                (db, ct) => db.Probes.CountAsync(ct),
                AbortToken
            );
        }

        // then
        await using var next = await storeFactory.CreateDbContextAsync(AbortToken);
        next.Database.GetDbConnection().Should().BeSameAs(supplied);
    }

    [Fact]
    public async Task should_refuse_a_context_that_owns_the_connection_its_options_supply()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        await using var supplied = new SqliteConnection(_connectionString);
        var storeFactory = _PooledFactory(options => options.UseSqlite(supplied, contextOwnsConnection: true));
        await using var unit = await Factory.BeginAsync(owner, AbortToken);

        // when
        var act = () =>
            EntityFrameworkEnlistment.RunAsync(
                storeFactory,
                unit,
                "the probe store",
                "probe read",
                (db, ct) => db.Probes.CountAsync(ct),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*contextOwnsConnection: false*");
        unit.State.Should().Be(UnitOfWorkState.Active);
    }

    [Fact]
    public async Task should_refuse_a_retrying_context_outside_an_execution_strategy()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var storeFactory = _PooledFactory(options =>
            options
                .UseSqlite(_connectionString)
                .ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        await using var unit = await Factory.BeginAsync(owner, AbortToken);

        // when
        var act = () =>
            EntityFrameworkEnlistment.RunAsync(
                storeFactory,
                unit,
                "the probe store",
                "probe read",
                (db, ct) => db.Probes.CountAsync(ct),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*RunAsync(db, …)*");
    }

    [Fact]
    public async Task should_refuse_a_unit_on_another_database()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var storeFactory = _PooledFactory(options =>
            options.UseSqlite(
                $"Data Source={Path.Combine(AppContext.BaseDirectory, $"other-{Guid.NewGuid():N}.db")};Pooling=False"
            )
        );
        await using var unit = await Factory.BeginAsync(owner, AbortToken);

        // when
        var act = () =>
            EntityFrameworkEnlistment.RunAsync(
                storeFactory,
                unit,
                "the probe store",
                "probe read",
                (db, ct) => db.Probes.CountAsync(ct),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*the probe store is configured for*");
    }

    [Fact]
    public async Task should_refuse_a_unit_without_a_relational_resource()
    {
        // given
        var storeFactory = _PooledFactory(options => options.UseSqlite(_connectionString));
        await using var unit = await Factory.BeginAsync(AbortToken);

        // when
        var act = () =>
            EntityFrameworkEnlistment.RunAsync(
                storeFactory,
                unit,
                "the probe store",
                "probe read",
                (db, ct) => db.Probes.CountAsync(ct),
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no relational resource*");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await _unitConnection.DisposeAsync();
        File.Delete(_databasePath);
        await base.DisposeAsyncCore();
    }

    /// <summary>The unit's owner: a context over the open connection that keeps the shared database alive.</summary>
    private async Task<ProbeDbContext> _CreateOwnerAsync()
    {
        await _unitConnection.OpenAsync(AbortToken);
        var owner = new ProbeDbContext(
            new DbContextOptionsBuilder<ProbeDbContext>().UseSqlite(_unitConnection).Options
        );
        await owner.Database.EnsureCreatedAsync(AbortToken);

        return owner;
    }

    private static PooledDbContextFactory<ProbeDbContext> _PooledFactory(
        Action<DbContextOptionsBuilder<ProbeDbContext>> configure
    )
    {
        var builder = new DbContextOptionsBuilder<ProbeDbContext>();
        configure(builder);

        return new PooledDbContextFactory<ProbeDbContext>(builder.Options, poolSize: 1);
    }

    private async Task<int> _CountAsync()
    {
        await using var db = new ProbeDbContext(
            new DbContextOptionsBuilder<ProbeDbContext>().UseSqlite(_connectionString).Options
        );

        return await db.Probes.CountAsync(AbortToken);
    }
}
#pragma warning restore CA2025
