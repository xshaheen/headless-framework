// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Caching;
using Headless.Hosting;
using Headless.Settings;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Storage;

/// <summary>
/// The EF setting value repository's enlisted members on a real SQLite unit: they read the unit's own uncommitted rows, roll
/// back with it, and clear the value cache only after it commits.
/// </summary>
public sealed class EfSettingValueRecordRepositoryUnitOfWorkTests : TestBase
{
    // A file under the test output, not an in-memory database or the temp directory: an open SQLite connection reports
    // no data source for memory and the resolved path for a file, so neither would match the configured data source.
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, $"settings-{Guid.NewGuid():N}.db");

    private readonly ICache<SettingValueCacheItem> _cache = Substitute.For<ICache<SettingValueCacheItem>>();
    private readonly SqliteConnection _unitConnection;
    private readonly ServiceProvider _services;
    private readonly SaveObserver _saveObserver = new();
    private readonly CommitFault _commitFault = new();
    private bool _useRetryStrategy;

    public EfSettingValueRecordRepositoryUnitOfWorkTests()
    {
        var connectionString = $"Data Source={_databasePath};Pooling=False";
        _unitConnection = new SqliteConnection(connectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<SettingsDbContext>(options =>
        {
            options.UseSqlite(connectionString).AddInterceptors(_saveObserver, _commitFault);
            if (_useRetryStrategy)
            {
                options.ReplaceService<IExecutionStrategyFactory, RetryStrategyFactory>();
            }
        });
        services.AddHeadlessSettings(setup => setup.UseEntityFramework<SettingsDbContext>());
        services.AddSingleton(_cache);
        _services = services.BuildServiceProvider();
    }

    private ISettingValueRecordRepository Repository => _services.GetRequiredService<ISettingValueRecordRepository>();

    private IUnitOfWorkFactory Factory => _services.GetRequiredService<IUnitOfWorkFactory>();

    [Fact]
    public async Task should_see_its_own_writes_inside_the_unit_and_discard_them_on_rollback()
    {
        // given
        await using var owner = await _CreateOwnerAsync();

        // when
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.SaveEnlistedAsync(unit, [_Value("App.Theme"), _Value("App.Locale")], [], [], AbortToken);

            (await Repository.GetListEnlistedAsync(unit, "Tenant", "t1", AbortToken)).Should().HaveCount(2);
            (await Repository.GetListEnlistedAsync(unit, ["App.Theme"], "Tenant", "t1", AbortToken))
                .Should()
                .ContainSingle();

            await unit.RollbackAsync();
        }

        // then
        (await Repository.GetListAsync("Tenant", "t1", AbortToken))
            .Should()
            .BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_commit_inserts_updates_and_deletes_and_clear_the_cache_after_each_commit()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var value = _Value("App.Theme");

        // when a unit inserts
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.SaveEnlistedAsync(unit, [value], [], [], AbortToken);
            _cache.ReceivedCalls().Should().BeEmpty();
            await unit.CompleteAsync(AbortToken);
        }

        // and a second unit updates it
        value.Value = "light";
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.SaveEnlistedAsync(unit, [], [value], [], AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        var afterUpdate = await Repository.GetListAsync("Tenant", "t1", AbortToken);

        // and a third deletes it
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.SaveEnlistedAsync(unit, [], [], [value], AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then
        afterUpdate.Should().ContainSingle().Which.Value.Should().Be("light");
        (await Repository.GetListAsync("Tenant", "t1", AbortToken)).Should().BeEmpty();
        var key = SettingValueCacheItem.CalculateCacheKey("App.Theme", "Tenant", "t1");
        await _cache
            .Received(3)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, SettingValueCacheItem>>(items => items.Keys.Single() == key),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_mark_an_observed_unit_not_replayable_and_commit_with_the_callers_transaction()
    {
        // given a transaction the caller commits itself
        await using var owner = await _CreateOwnerAsync();
        await using var transaction = await owner.Database.BeginTransactionAsync(AbortToken);
        await using var unit = Factory.Enlist(owner, transaction);

        // when
        await Repository.SaveEnlistedAsync(unit, [_Value("App.Theme")], [], [], AbortToken);
        await transaction.CommitAsync(AbortToken);
        await unit.CompleteAsync(AbortToken);

        // then
        unit.IsRetryPrevented.Should().BeTrue();
        (await Repository.GetListAsync("Tenant", "t1", AbortToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task should_write_the_stored_value_to_the_cache_on_a_direct_write()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var value = _Value("App.Theme");
        var key = SettingValueCacheItem.CalculateCacheKey("App.Theme", "Tenant", "t1");
        var written = new List<string?>();
        _cache
            .When(x =>
                x.UpsertAllAsync(
                    Arg.Any<IDictionary<string, SettingValueCacheItem>>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call => written.Add(call.Arg<IDictionary<string, SettingValueCacheItem>>()[key].Value));

        // when the row is inserted, updated, saved in a batch, and deleted outside a unit
        await Repository.InsertAsync(value, AbortToken);
        value.Value = "light";
        await Repository.UpdateAsync(value, AbortToken);
        value.Value = "blue";
        await Repository.SaveAsync([], [value], [], AbortToken);
        await Repository.DeleteAsync([value], AbortToken);

        // then each write stores what it wrote (a delete stores an empty entry) instead of removing the entry
        written.Should().Equal("dark", "light", "blue", null);
        await _cache.DidNotReceive().RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_save_a_versioned_group_only_while_it_keeps_its_version()
    {
        // given a stored value and the version of its group
        await using var owner = await _CreateOwnerAsync();
        var stored = _Value("App.Theme");
        await Repository.InsertAsync(stored, AbortToken);
        HashSet<string> names = ["App.Theme", "App.Locale"];
        var version = SettingValueVersion.Compute(
            "Tenant",
            "t1",
            names,
            await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)
        );
        var added = _Value("App.Locale");
        stored.Value = "light";

        // when one save presents the version, and a second presents the same, now stale, one
        await Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [added], [stored], [], AbortToken);
        var replay = () => Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [], [], [added], AbortToken);

        // then the first commits, and the second fails and writes nothing
        await replay.Should().ThrowExactlyAsync<DBConcurrencyException>();
        var current = await Repository.GetListAsync(names, "Tenant", "t1", AbortToken);
        current
            .Select(x => (x.Name, x.Value))
            .Should()
            .BeEquivalentTo([("App.Theme", "light"), ("App.Locale", "dark")]);
        SettingValueVersion.Compute("Tenant", "t1", names, current).Should().NotBe(version);
    }

    [Fact]
    public async Task should_return_the_committed_version_even_when_cache_maintenance_throws_a_concurrency_exception()
    {
        await using var owner = await _CreateOwnerAsync();
        HashSet<string> names = ["App.Theme"];
        var version = SettingValueVersion.Compute("Tenant", "t1", names, []);
        var stored = _Value("App.Theme");
        _cache
            .When(x =>
                x.UpsertAllAsync(
                    Arg.Any<IDictionary<string, SettingValueCacheItem>>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => throw new DBConcurrencyException("cache unavailable after commit"));

        var committedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Tenant",
            "t1",
            version,
            [stored],
            [],
            [],
            AbortToken
        );

        committedVersion
            .Should()
            .Be(
                SettingValueVersion.Compute(
                    "Tenant",
                    "t1",
                    names,
                    await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)
                )
            );
        committedVersion.Should().NotBe(version);
        _saveObserver.Saves.Should().Be(1);
    }

    [Fact]
    public async Task should_write_nothing_for_an_unchanged_group_and_accept_the_original_absent_version_after_clear()
    {
        await using var owner = await _CreateOwnerAsync();
        HashSet<string> names = ["App.Theme"];
        var absentVersion = SettingValueVersion.Compute("Tenant", "t1", names, []);
        var stored = _Value("App.Theme");
        var addedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Tenant",
            "t1",
            absentVersion,
            [stored],
            [],
            [],
            AbortToken
        );
        _saveObserver.Saves = 0;

        var unchangedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Tenant",
            "t1",
            addedVersion,
            [],
            [],
            [],
            AbortToken
        );
        unchangedVersion.Should().Be(addedVersion);
        _saveObserver.Saves.Should().Be(0);

        var clearedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Tenant",
            "t1",
            unchangedVersion,
            [],
            [],
            [stored],
            AbortToken
        );
        clearedVersion.Should().Be(absentVersion);
        var restored = await Repository.SaveIfVersionAsync(
            names,
            "Tenant",
            "t1",
            absentVersion,
            [stored],
            [],
            [],
            AbortToken
        );
        restored
            .Should()
            .Be(
                SettingValueVersion.Compute(
                    "Tenant",
                    "t1",
                    names,
                    await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)
                )
            );
    }

    [Theory]
    [InlineData(5, 5, 1, 2, true)]
    [InlineData(19, 2067, 1, 2, true)]
    [InlineData(1, 1, 1, 1, false)]
    [InlineData(5, 5, 3, 3, false)]
    public async Task should_retry_only_precommit_conflicts_on_fresh_contexts_with_a_bound(
        int errorCode,
        int extendedCode,
        int failures,
        int expectedAttempts,
        bool succeeds
    )
    {
        _useRetryStrategy = true;
        await using var owner = await _CreateOwnerAsync();
        HashSet<string> names = ["App.Theme"];
        var version = SettingValueVersion.Compute("Tenant", "t1", names, []);
        _saveObserver.Failure = new SqliteException("injected database failure", errorCode, extendedCode);
        _saveObserver.FailuresRemaining = failures;

        var save = () =>
            Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [_Value("App.Theme")], [], [], AbortToken);
        if (succeeds)
        {
            var committed = await save();
            var rows = await Repository.GetListAsync(names, "Tenant", "t1", AbortToken);
            committed.Should().Be(SettingValueVersion.Compute("Tenant", "t1", names, rows));
            rows.Should().ContainSingle();
        }
        else
        {
            await save.Should().ThrowExactlyAsync<SqliteException>();
            (await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)).Should().BeEmpty();
        }

        _saveObserver.Saves.Should().Be(expectedAttempts);
        _saveObserver.Contexts.Should().HaveCount(expectedAttempts);
    }

    [Fact]
    public async Task should_not_replay_a_lost_commit_reply_even_under_a_retrying_execution_strategy()
    {
        _useRetryStrategy = true;
        await using var owner = await _CreateOwnerAsync();
        HashSet<string> names = ["App.Theme"];
        var version = SettingValueVersion.Compute("Tenant", "t1", names, []);
        _commitFault.Armed = true;

        var save = () =>
            Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [_Value("App.Theme")], [], [], AbortToken);

        await save.Should().ThrowExactlyAsync<SqliteException>();
        _commitFault.Commits.Should().Be(1);
        _saveObserver.Saves.Should().Be(1);
        (await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)).Should().ContainSingle();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await _unitConnection.DisposeAsync();
        File.Delete(_databasePath);
        await base.DisposeAsyncCore();
    }

    private async Task<SettingsDbContext> _CreateOwnerAsync()
    {
        await _unitConnection.OpenAsync(AbortToken);
        var owner = new SettingsDbContext(
            new DbContextOptionsBuilder<SettingsDbContext>().UseSqlite(_unitConnection).Options
        );
        await owner.Database.EnsureCreatedAsync(AbortToken);

        return owner;
    }

    private static SettingValueRecord _Value(string name)
    {
        return new SettingValueRecord(Guid.NewGuid(), name, "dark", "Tenant", "t1");
    }

    // A retrying strategy would replay both injected faults if the repository let them escape its delegate.
    private sealed class RetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SqliteException;
    }

    private sealed class RetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryStrategy(dependencies);
    }

    private sealed class CommitFault : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Commits { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Commits++;
            if (Armed)
            {
                Armed = false;
                throw new SqliteException("lost commit reply", 5);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class SaveObserver : SaveChangesInterceptor
    {
        public int Saves { get; set; }
        public Exception? Failure { get; set; }
        public int FailuresRemaining { get; set; }
        public HashSet<Guid> Contexts { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Saves++;
            if (eventData.Context is { } context)
            {
                Contexts.Add(context.ContextId.InstanceId);
            }

            if (FailuresRemaining > 0 && Failure is { } failure)
            {
                FailuresRemaining--;
                throw failure;
            }

            return ValueTask.FromResult(result);
        }
    }

    public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessSettings(new SettingsStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
