// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless;
using Headless.Caching;
using Headless.Features;
using Headless.Hosting;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Storage;

/// <summary>
/// The EF feature value repository's enlisted members on a real SQLite unit: they read the unit's own uncommitted rows, roll
/// back with it, and clear the value cache only after it commits.
/// </summary>
public sealed class EfFeatureValueRecordRepositoryUnitOfWorkTests : TestBase
{
    // A file under the test output, not an in-memory database or the temp directory: an open SQLite connection reports
    // no data source for memory and the resolved path for a file, so neither would match the configured data source.
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, $"features-{Guid.NewGuid():N}.db");

    private readonly ICache _cache = Substitute.For<ICache>();
    private readonly SqliteConnection _unitConnection;
    private readonly ServiceProvider _services;
    private readonly SaveObserver _saveObserver = new();
    private readonly CommitFault _commitFault = new();
    private bool _useRetryStrategy;

    public EfFeatureValueRecordRepositoryUnitOfWorkTests()
    {
        _cache
            .GetAllAsync<FeatureValueCacheItem>(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CacheValue<FeatureValueCacheItem>>(StringComparer.Ordinal));
        _cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<FeatureValueCacheItem>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var connectionString = $"Data Source={_databasePath};Pooling=False";
        _unitConnection = new SqliteConnection(connectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<FeaturesDbContext>(options =>
        {
            options.UseSqlite(connectionString).AddInterceptors(_saveObserver, _commitFault);
            if (_useRetryStrategy)
            {
                options.ReplaceService<IExecutionStrategyFactory, RetryStrategyFactory>();
            }
        });
        services.AddHeadlessFeatures(setup => setup.UseEntityFramework<FeaturesDbContext>());
        services.AddSingleton(_cache);
        _services = services.BuildServiceProvider();
    }

    private IFeatureValueRecordRepository Repository => _services.GetRequiredService<IFeatureValueRecordRepository>();

    private IUnitOfWorkFactory Factory => _services.GetRequiredService<IUnitOfWorkFactory>();

    [Fact]
    public async Task should_see_its_own_writes_inside_the_unit_and_discard_them_on_rollback()
    {
        // given
        await using var owner = await _CreateOwnerAsync();

        // when
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.SaveEnlistedAsync(unit, [_Value("Reports"), _Value("Exports")], [], [], AbortToken);

            (await Repository.GetListEnlistedAsync(unit, "Tenant", "t1", AbortToken)).Should().HaveCount(2);
            (await Repository.GetListEnlistedAsync(unit, ["Reports"], "Tenant", "t1", AbortToken))
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
    public async Task should_refresh_each_affected_provider_key_once_on_a_store_delete()
    {
        const string name = "Reports";
        await using var owner = await _CreateOwnerAsync();
        await Repository.InsertAsync(_Value(name), AbortToken);
        await Repository.InsertAsync(new FeatureValueRecord(Guid.NewGuid(), name, "light", "Tenant", null), AbortToken);
        _cache.ClearReceivedCalls();
        var store = new FeatureValueStore(
            Substitute.For<IFeatureDefinitionManager>(),
            Repository,
            Substitute.For<IGuidGenerator>(),
            _cache,
            NullLogger<FeatureValueStore>.Instance
        );

        await store.DeleteAsync(name, "Tenant", providerKey: null, AbortToken);

        (await Repository.FindAllAsync(name, "Tenant", null, AbortToken)).Should().BeEmpty();
        await _cache
            .Received(1)
            .TryInsertAsync(
                FeatureValueCacheItem.CalculateCacheKey(name, "Tenant", null),
                Arg.Is<FeatureValueCacheItem>(item => item.Value == null),
                Arg.Any<TimeSpan?>(),
                CancellationToken.None
            );
        await _cache
            .Received(1)
            .TryInsertAsync(
                FeatureValueCacheItem.CalculateCacheKey(name, "Tenant", "t1"),
                Arg.Is<FeatureValueCacheItem>(item => item.Value == null),
                Arg.Any<TimeSpan?>(),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task should_commit_inserts_updates_and_deletes_and_clear_the_cache_after_each_commit()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var value = _Value("Reports");

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
        var key = FeatureValueCacheItem.CalculateCacheKey("Reports", "Tenant", "t1");
        await _cache
            .Received(3)
            .TryInsertAsync(key, Arg.Any<FeatureValueCacheItem>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_mark_an_observed_unit_not_replayable_and_commit_with_the_callers_transaction()
    {
        // given a transaction the caller commits itself
        await using var owner = await _CreateOwnerAsync();
        await using var transaction = await owner.Database.BeginTransactionAsync(AbortToken);
        await using var unit = Factory.Enlist(owner, transaction);

        // when
        await Repository.SaveEnlistedAsync(unit, [_Value("Reports")], [], [], AbortToken);
        await transaction.CommitAsync(AbortToken);
        await unit.CompleteAsync(AbortToken);

        // then
        unit.IsRetryPrevented.Should().BeTrue();
        (await Repository.GetListAsync("Tenant", "t1", AbortToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task should_save_a_versioned_group_only_while_it_keeps_its_version()
    {
        // given a stored value and the version of its group
        await using var owner = await _CreateOwnerAsync();
        var stored = _Value("Reports");
        await Repository.InsertAsync(stored, AbortToken);
        HashSet<string> names = ["Reports", "Exports"];
        var version = FeatureValueVersion.Compute(
            "Tenant",
            "t1",
            names,
            await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)
        );
        var added = _Value("Exports");
        stored.Value = "light";

        // when one save presents the version, and a second presents the same, now stale, one
        await Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [added], [stored], [], AbortToken);
        var replay = () => Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [], [], [added], AbortToken);

        // then the first commits, and the second fails and writes nothing
        await replay.Should().ThrowExactlyAsync<DBConcurrencyException>();
        var current = await Repository.GetListAsync(names, "Tenant", "t1", AbortToken);
        current.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Reports", "light"), ("Exports", "dark")]);
        FeatureValueVersion.Compute("Tenant", "t1", names, current).Should().NotBe(version);
    }

    [Fact]
    public async Task should_return_the_committed_version_even_when_cache_maintenance_throws_a_concurrency_exception()
    {
        await using var owner = await _CreateOwnerAsync();
        HashSet<string> names = ["App.Theme"];
        var version = FeatureValueVersion.Compute("Tenant", "t1", names, []);
        var stored = _Value("App.Theme");
        _cache
            .When(x =>
                x.TryInsertAsync(
                    Arg.Any<string>(),
                    Arg.Any<FeatureValueCacheItem>(),
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
                FeatureValueVersion.Compute(
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
        var absentVersion = FeatureValueVersion.Compute("Tenant", "t1", names, []);
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
                FeatureValueVersion.Compute(
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
        var version = FeatureValueVersion.Compute("Tenant", "t1", names, []);
        _saveObserver.Failure = new SqliteException("injected database failure", errorCode, extendedCode);
        _saveObserver.FailuresRemaining = failures;

        var save = () =>
            Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [_Value("App.Theme")], [], [], AbortToken);
        if (succeeds)
        {
            var committed = await save();
            var rows = await Repository.GetListAsync(names, "Tenant", "t1", AbortToken);
            committed.Should().Be(FeatureValueVersion.Compute("Tenant", "t1", names, rows));
            rows.Should().ContainSingle();
        }
        else
        {
            // A conflict that outlasts every attempt is reported as a version conflict, so the caller re-reads.
            if (errorCode == 1)
            {
                await save.Should().ThrowExactlyAsync<SqliteException>();
            }
            else
            {
                (await save.Should().ThrowExactlyAsync<DBConcurrencyException>())
                    .Which.InnerException.Should()
                    .BeOfType<SqliteException>();
            }

            (await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)).Should().BeEmpty();
        }

        _saveObserver.Saves.Should().Be(expectedAttempts);
        _saveObserver.Contexts.Should().HaveCount(expectedAttempts);
    }

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(3, 3, false)]
    public async Task should_retry_a_commit_serialization_failure_on_a_fresh_context(
        int failures,
        int attempts,
        bool succeeds
    )
    {
        await using var owner = await _CreateOwnerAsync();
        HashSet<string> names = ["App.Theme"];
        var version = FeatureValueVersion.Compute("Tenant", "t1", names, []);
        _commitFault.ConflictsRemaining = failures;
        var save = () =>
            Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [_Value("App.Theme")], [], [], AbortToken);

        if (succeeds)
        {
            var savedVersion = await save();
            var rows = await Repository.GetListAsync(names, "Tenant", "t1", AbortToken);
            rows.Should().ContainSingle();
            savedVersion.Should().Be(FeatureValueVersion.Compute("Tenant", "t1", names, rows));
        }
        else
        {
            (await save.Should().ThrowExactlyAsync<DBConcurrencyException>())
                .Which.InnerException.Should()
                .BeOfType<CommitConflictException>();
            (await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)).Should().BeEmpty();
        }

        _saveObserver.Saves.Should().Be(attempts);
        _saveObserver.Contexts.Should().HaveCount(attempts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_reconcile_a_lost_commit_reply_without_replay_even_under_a_retrying_execution_strategy(
        bool committed
    )
    {
        _useRetryStrategy = true;
        await using var owner = await _CreateOwnerAsync();
        HashSet<string> names = ["App.Theme"];
        var version = FeatureValueVersion.Compute("Tenant", "t1", names, []);
        _commitFault.Armed = true;
        _commitFault.FailBeforeCommit = !committed;

        var save = () =>
            Repository.SaveIfVersionAsync(names, "Tenant", "t1", version, [_Value("App.Theme")], [], [], AbortToken);

        if (committed)
        {
            var savedVersion = await save();
            var rows = await Repository.GetListAsync(names, "Tenant", "t1", AbortToken);
            rows.Should().ContainSingle();
            savedVersion.Should().Be(FeatureValueVersion.Compute("Tenant", "t1", names, rows));
        }
        else
        {
            (await save.Should().ThrowExactlyAsync<IOException>()).Which.Should().BeSameAs(_commitFault.Failure);
            (await Repository.GetListAsync(names, "Tenant", "t1", AbortToken)).Should().BeEmpty();
            _cache.ReceivedCalls().Should().BeEmpty();
        }

        _commitFault.Commits.Should().Be(committed ? 1 : 0);
        _saveObserver.Saves.Should().Be(1);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        await _unitConnection.DisposeAsync();
        File.Delete(_databasePath);
        await base.DisposeAsyncCore();
    }

    private async Task<FeaturesDbContext> _CreateOwnerAsync()
    {
        await _unitConnection.OpenAsync(AbortToken);
        var owner = new FeaturesDbContext(
            new DbContextOptionsBuilder<FeaturesDbContext>().UseSqlite(_unitConnection).Options
        );
        await owner.Database.EnsureCreatedAsync(AbortToken);

        return owner;
    }

    private static FeatureValueRecord _Value(string name)
    {
        return new FeatureValueRecord(Guid.NewGuid(), name, "dark", "Tenant", "t1");
    }

    // A retrying strategy would replay both injected faults if the repository let them escape its delegate.
    private sealed class RetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is SqliteException or IOException;
    }

    private sealed class RetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryStrategy(dependencies);
    }

    private sealed class CommitConflictException() : DbException("serialization rejected")
    {
        public override string SqlState => "40001";
    }

    private sealed class CommitFault : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public bool FailBeforeCommit { get; set; }
        public int ConflictsRemaining { get; set; }
        public IOException Failure { get; } = new("lost commit reply");
        public int Commits { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (ConflictsRemaining > 0)
            {
                ConflictsRemaining--;
                throw new CommitConflictException();
            }

            if (Armed && FailBeforeCommit)
            {
                Armed = false;
                throw Failure;
            }

            return ValueTask.FromResult(result);
        }

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
                throw Failure;
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

    public sealed class FeaturesDbContext(DbContextOptions<FeaturesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessFeatures(new FeaturesStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
