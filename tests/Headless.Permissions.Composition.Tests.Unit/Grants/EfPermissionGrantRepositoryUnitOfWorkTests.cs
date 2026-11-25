// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless;
using Headless.Caching;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests.Grants;

/// <summary>
/// The EF grant repository's enlisted members on a real SQLite unit: they read the unit's own uncommitted rows, roll
/// back with it, and write the grant cache only after it commits.
/// </summary>
public sealed class EfPermissionGrantRepositoryUnitOfWorkTests : TestBase
{
    // A file under the test output, not an in-memory database or the temp directory: an open SQLite connection reports
    // no data source for memory and the resolved path for a file, so neither would match the configured data source.
    private readonly string _databasePath = Path.Combine(
        AppContext.BaseDirectory,
        $"permissions-{Guid.NewGuid():N}.db"
    );

    private readonly ICache<PermissionGrantCacheItem> _cache = Substitute.For<ICache<PermissionGrantCacheItem>>();
    private readonly SqliteConnection _unitConnection;
    private readonly ServiceProvider _services;
    private readonly SaveObserver _saveObserver = new();
    private readonly CommitFault _commitFault = new();
    private bool _useRetryStrategy;

    public EfPermissionGrantRepositoryUnitOfWorkTests()
    {
        _cache
            .GetAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CacheValue<PermissionGrantCacheItem>>(StringComparer.Ordinal));
        _cache
            .TryInsertAsync(
                Arg.Any<string>(),
                Arg.Any<PermissionGrantCacheItem>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        var connectionString = $"Data Source={_databasePath};Pooling=False";
        _unitConnection = new SqliteConnection(connectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<GrantsDbContext>(options =>
        {
            options.UseSqlite(connectionString).AddInterceptors(_saveObserver, _commitFault);
            if (_useRetryStrategy)
            {
                options.ReplaceService<IExecutionStrategyFactory, RetryStrategyFactory>();
            }
        });
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<GrantsDbContext>());
        services.AddSingleton(_cache);
        _services = services.BuildServiceProvider();
    }

    private IPermissionGrantRepository Repository => _services.GetRequiredService<IPermissionGrantRepository>();

    private IUnitOfWorkFactory Factory => _services.GetRequiredService<IUnitOfWorkFactory>();

    [Fact]
    public async Task should_see_its_own_writes_inside_the_unit_and_discard_them_on_rollback()
    {
        // given
        await using var owner = await _CreateOwnerAsync();

        // when
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.InsertManyEnlistedAsync(
                unit,
                [_Grant("Users.Create"), _Grant("Users.Delete")],
                AbortToken
            );

            (await Repository.GetListEnlistedAsync(unit, "Role", "admin", AbortToken)).Should().HaveCount(2);
            (await Repository.GetListEnlistedAsync(unit, ["Users.Create"], "Role", "admin", AbortToken))
                .Should()
                .ContainSingle();

            await unit.RollbackAsync();
        }

        // then
        (await Repository.GetListAsync("Role", "admin", AbortToken))
            .Should()
            .BeEmpty();
        _cache.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_commit_inserts_and_deletes_and_write_the_cache_after_each_commit()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var grant = _Grant("Users.Create");
        var key = PermissionGrantCacheItem.CalculateCacheKey("Users.Create", "Role", "admin");
        var written = _RecordCacheWrites(key);

        // when a unit inserts
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.InsertManyEnlistedAsync(unit, [grant], AbortToken);
            _cache.ReceivedCalls().Should().BeEmpty();
            await unit.CompleteAsync(AbortToken);
        }

        var afterInsert = await Repository.GetListAsync("Role", "admin", AbortToken);

        // and a second unit deletes
        await using (var unit = await Factory.BeginAsync(owner, AbortToken))
        {
            await Repository.DeleteManyEnlistedAsync(unit, [grant], AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then each commit stores the committed status (undefined for the delete) instead of removing the entry
        afterInsert.Should().ContainSingle();
        (await Repository.GetListAsync("Role", "admin", AbortToken)).Should().BeEmpty();
        written.Should().Equal(true, null);
        await _cache.DidNotReceive().RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_refresh_the_replacement_grant_once_after_a_store_write_commits()
    {
        const string name = "Users.Create";
        const string provider = PermissionGrantProviderNames.Role;
        const string role = "admin";
        await using var owner = await _CreateOwnerAsync();
        await Repository.InsertAsync(_Grant(name), AbortToken);
        _cache.ClearReceivedCalls();
        var written = _RecordCacheWrites(PermissionGrantCacheItem.CalculateCacheKey(name, provider, role));
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        var store = new PermissionGrantStore(
            Substitute.For<IPermissionDefinitionManager>(),
            Repository,
            guidGenerator,
            _cache,
            _services.GetRequiredService<ICurrentTenant>(),
            Options.Create(new PermissionManagementOptions()),
            NullLogger<PermissionGrantStore>.Instance
        );
        await using var unit = await Factory.BeginAsync(owner, AbortToken);

        await store.RevokeEnlistedAsync(unit, [name], provider, role, AbortToken);
        written.Should().BeEmpty();
        await unit.CompleteAsync(AbortToken);

        written.Should().Equal(false);
    }

    [Fact]
    public async Task should_keep_a_later_store_revoke_when_the_enlisted_repository_refreshes_after_it()
    {
        const string name = "Users.Create";
        const string provider = PermissionGrantProviderNames.Role;
        const string role = "admin";
        var key = PermissionGrantCacheItem.CalculateCacheKey(name, provider, role);
        var written = _RecordCacheWrites(key);
        // Record unconditional writes too, so an older grant would appear after the committed denial.
        _cache
            .When(cache =>
                cache.UpsertAllAsync(
                    Arg.Any<IDictionary<string, PermissionGrantCacheItem>>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call => written.Add(call.Arg<IDictionary<string, PermissionGrantCacheItem>>()[key].IsGranted));
        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());
        var store = new PermissionGrantStore(
            Substitute.For<IPermissionDefinitionManager>(),
            Repository,
            guidGenerator,
            _cache,
            _services.GetRequiredService<ICurrentTenant>(),
            Options.Create(new PermissionManagementOptions()),
            NullLogger<PermissionGrantStore>.Instance
        );
        await using var owner = await _CreateOwnerAsync();
        await using var unit = await Factory.BeginAsync(owner, AbortToken);
        // This callback runs after the insert committed and before the earlier write refreshes its cache.
        using var revoke = unit.OnCompleted(() => new ValueTask(store.RevokeAsync(name, provider, role, AbortToken)));
        await Repository.InsertManyEnlistedAsync(unit, [_Grant(name)], AbortToken);

        await unit.CompleteAsync(AbortToken);

        var rows = await Repository.GetListAsync([name], provider, role, AbortToken);
        rows.Should().ContainSingle().Which.IsGranted.Should().BeFalse();
        written.Should().NotBeEmpty().And.OnlyContain(status => status == false);
    }

    [Fact]
    public async Task should_write_the_stored_status_to_the_cache_on_a_direct_write()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var grant = _Grant("Users.Create");
        var prohibited = new PermissionGrantRecord(Guid.NewGuid(), "Users.Delete", "Role", "admin", isGranted: false);
        var grantKey = PermissionGrantCacheItem.CalculateCacheKey("Users.Create", "Role", "admin");
        var prohibitedKey = PermissionGrantCacheItem.CalculateCacheKey("Users.Delete", "Role", "admin");
        var grantWrites = _RecordCacheWrites(grantKey);
        var prohibitedWrites = _RecordCacheWrites(prohibitedKey);

        // when the rows are inserted one by one and in a batch, then deleted, outside a unit
        await Repository.InsertAsync(grant, AbortToken);
        await Repository.InsertManyAsync([prohibited], AbortToken);
        await Repository.DeleteManyAsync([grant, prohibited], AbortToken);

        // then each write stores what it wrote (a delete stores an undefined status) instead of removing the entry
        grantWrites.Should().Equal(true, null);
        prohibitedWrites.Should().Equal(false, null);
        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cache.DidNotReceive().RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_write_the_replacement_status_to_the_cache_when_a_save_flips_a_grant()
    {
        // given
        await using var owner = await _CreateOwnerAsync();
        var grant = _Grant("Users.Create");
        var denial = new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", "Role", "admin", isGranted: false);
        var key = PermissionGrantCacheItem.CalculateCacheKey("Users.Create", "Role", "admin");
        await Repository.InsertAsync(grant, AbortToken);
        var written = _RecordCacheWrites(key);

        // when the grant is replaced by a denial in one save
        await Repository.SaveAsync([denial], [grant], AbortToken);

        // then the entry holds the denial; it is not removed, so a reader that loaded the grant cannot fill it back
        written.Should().Equal(false);
        await _cache.DidNotReceive().RemoveAllAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_mark_an_observed_unit_not_replayable_and_commit_with_the_callers_transaction()
    {
        // given a transaction the caller commits itself
        await using var owner = await _CreateOwnerAsync();
        await using var transaction = await owner.Database.BeginTransactionAsync(AbortToken);
        await using var unit = Factory.Enlist(owner, transaction);

        // when
        await Repository.InsertManyEnlistedAsync(unit, [_Grant("Users.Create")], AbortToken);
        await transaction.CommitAsync(AbortToken);
        await unit.CompleteAsync(AbortToken);

        // then
        unit.IsRetryPrevented.Should().BeTrue();
        (await Repository.GetListAsync("Role", "admin", AbortToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task should_save_a_versioned_group_only_while_it_keeps_its_version()
    {
        // given a stored grant and the version of its group
        await using var owner = await _CreateOwnerAsync();
        var granted = _Grant("Users.Create");
        await Repository.InsertAsync(granted, AbortToken);
        string[] names = ["Users.Create", "Users.Delete"];
        var version = PermissionGrantVersion.Compute(
            "Role",
            "admin",
            tenantId: null,
            names,
            await Repository.GetListAsync(names, "Role", "admin", AbortToken)
        );
        var prohibited = new PermissionGrantRecord(Guid.NewGuid(), "Users.Create", "Role", "admin", isGranted: false);

        // when one save turns the grant into a prohibition, and a second presents the same, now stale, version
        await Repository.SaveIfVersionAsync(names, "Role", "admin", version, [prohibited], [granted], AbortToken);
        var replay = () =>
            Repository.SaveIfVersionAsync(names, "Role", "admin", version, [_Grant("Users.Delete")], [], AbortToken);

        // then the first commits, and the second fails and writes nothing
        await replay.Should().ThrowExactlyAsync<DBConcurrencyException>();
        var current = await Repository.GetListAsync(names, "Role", "admin", AbortToken);
        current.Should().ContainSingle().Which.IsGranted.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_the_committed_version_even_when_cache_maintenance_throws_a_concurrency_exception()
    {
        await using var owner = await _CreateOwnerAsync();
        string[] names = ["Users.Create"];
        var version = PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, []);
        var stored = _Grant("Users.Create");
        _cache
            .When(x =>
                x.TryInsertAsync(
                    Arg.Any<string>(),
                    Arg.Any<PermissionGrantCacheItem>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => throw new DBConcurrencyException("cache unavailable after commit"));

        var committedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Role",
            "admin",
            version,
            [stored],
            [],
            AbortToken
        );

        committedVersion
            .Should()
            .Be(
                PermissionGrantVersion.Compute(
                    "Role",
                    "admin",
                    tenantId: null,
                    names,
                    await Repository.GetListAsync(names, "Role", "admin", AbortToken)
                )
            );
        committedVersion.Should().NotBe(version);
        _saveObserver.Saves.Should().Be(1);
    }

    [Fact]
    public async Task should_write_nothing_for_an_unchanged_group_and_accept_the_original_absent_version_after_clear()
    {
        await using var owner = await _CreateOwnerAsync();
        string[] names = ["Users.Create"];
        var absentVersion = PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, []);
        var stored = _Grant("Users.Create");
        var addedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Role",
            "admin",
            absentVersion,
            [stored],
            [],
            AbortToken
        );
        _saveObserver.Saves = 0;

        var unchangedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Role",
            "admin",
            addedVersion,
            [],
            [],
            AbortToken
        );
        unchangedVersion.Should().Be(addedVersion);
        _saveObserver.Saves.Should().Be(0);

        var clearedVersion = await Repository.SaveIfVersionAsync(
            names,
            "Role",
            "admin",
            unchangedVersion,
            [],
            [stored],
            AbortToken
        );
        clearedVersion.Should().Be(absentVersion);
        var restored = await Repository.SaveIfVersionAsync(
            names,
            "Role",
            "admin",
            absentVersion,
            [stored],
            [],
            AbortToken
        );
        restored
            .Should()
            .Be(
                PermissionGrantVersion.Compute(
                    "Role",
                    "admin",
                    tenantId: null,
                    names,
                    await Repository.GetListAsync(names, "Role", "admin", AbortToken)
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
        string[] names = ["Users.Create"];
        var version = PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, []);
        _saveObserver.Failure = new SqliteException("injected database failure", errorCode, extendedCode);
        _saveObserver.FailuresRemaining = failures;

        var save = () =>
            Repository.SaveIfVersionAsync(names, "Role", "admin", version, [_Grant("Users.Create")], [], AbortToken);
        if (succeeds)
        {
            var committed = await save();
            var rows = await Repository.GetListAsync(names, "Role", "admin", AbortToken);
            committed.Should().Be(PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, rows));
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

            (await Repository.GetListAsync(names, "Role", "admin", AbortToken)).Should().BeEmpty();
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
        string[] names = ["Users.Create"];
        var version = PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, []);
        _commitFault.ConflictsRemaining = failures;
        var save = () =>
            Repository.SaveIfVersionAsync(names, "Role", "admin", version, [_Grant("Users.Create")], [], AbortToken);

        if (succeeds)
        {
            var savedVersion = await save();
            var rows = await Repository.GetListAsync(names, "Role", "admin", AbortToken);
            rows.Should().ContainSingle();
            savedVersion.Should().Be(PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, rows));
        }
        else
        {
            (await save.Should().ThrowExactlyAsync<DBConcurrencyException>())
                .Which.InnerException.Should()
                .BeOfType<CommitConflictException>();
            (await Repository.GetListAsync(names, "Role", "admin", AbortToken)).Should().BeEmpty();
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
        string[] names = ["Users.Create"];
        var version = PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, []);
        _commitFault.Armed = true;
        _commitFault.FailBeforeCommit = !committed;

        var save = () =>
            Repository.SaveIfVersionAsync(names, "Role", "admin", version, [_Grant("Users.Create")], [], AbortToken);

        if (committed)
        {
            var savedVersion = await save();
            var rows = await Repository.GetListAsync(names, "Role", "admin", AbortToken);
            rows.Should().ContainSingle();
            savedVersion.Should().Be(PermissionGrantVersion.Compute("Role", "admin", tenantId: null, names, rows));
        }
        else
        {
            (await save.Should().ThrowExactlyAsync<IOException>()).Which.Should().BeSameAs(_commitFault.Failure);
            (await Repository.GetListAsync(names, "Role", "admin", AbortToken)).Should().BeEmpty();
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

    private async Task<GrantsDbContext> _CreateOwnerAsync()
    {
        await _unitConnection.OpenAsync(AbortToken);
        var owner = new GrantsDbContext(
            new DbContextOptionsBuilder<GrantsDbContext>().UseSqlite(_unitConnection).Options
        );
        await owner.Database.EnsureCreatedAsync(AbortToken);

        return owner;
    }

    private static PermissionGrantRecord _Grant(string name)
    {
        return new PermissionGrantRecord(Guid.NewGuid(), name, "Role", "admin", isGranted: true);
    }

    /// <summary>Collects the status each cache write stores under <paramref name="key"/>, in call order.</summary>
    private List<bool?> _RecordCacheWrites(string key)
    {
        var written = new List<bool?>();
        _cache
            .When(x =>
                x.TryInsertAsync(
                    key,
                    Arg.Any<PermissionGrantCacheItem>(),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call => written.Add(call.Arg<PermissionGrantCacheItem>().IsGranted));

        return written;
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

    public sealed class GrantsDbContext(DbContextOptions<GrantsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessPermissions(new PermissionsStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
