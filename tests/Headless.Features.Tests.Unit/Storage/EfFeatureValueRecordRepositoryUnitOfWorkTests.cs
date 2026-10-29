// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Features;
using Headless.Hosting;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    public EfFeatureValueRecordRepositoryUnitOfWorkTests()
    {
        var connectionString = $"Data Source={_databasePath};Pooling=False";
        _unitConnection = new SqliteConnection(connectionString);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<FeaturesDbContext>(options => options.UseSqlite(connectionString));
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
            .RemoveAllAsync(Arg.Is<IEnumerable<string>>(keys => keys.Single() == key), Arg.Any<CancellationToken>());
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

    public sealed class FeaturesDbContext(DbContextOptions<FeaturesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessFeatures(new FeaturesStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
