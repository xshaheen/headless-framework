// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    public EfPermissionGrantRepositoryUnitOfWorkTests()
    {
        var connectionString = $"Data Source={_databasePath};Pooling=False";
        _unitConnection = new SqliteConnection(connectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<GrantsDbContext>(options => options.UseSqlite(connectionString));
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
                x.UpsertAllAsync(
                    Arg.Is<IDictionary<string, PermissionGrantCacheItem>>(items => items.ContainsKey(key)),
                    Arg.Any<TimeSpan?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call => written.Add(call.Arg<IDictionary<string, PermissionGrantCacheItem>>()[key].IsGranted));

        return written;
    }

    public sealed class GrantsDbContext(DbContextOptions<GrantsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessPermissions(new PermissionsStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
