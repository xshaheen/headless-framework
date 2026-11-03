// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Features;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Storage;

/// <summary>
/// With <see cref="FeatureManagementOptions.FeatureValueCacheName"/> set, a direct repository write updates the entry
/// in the named cache the value store reads, not in the default cache.
/// </summary>
public sealed class EfFeatureValueRecordRepositoryNamedCacheTests : TestBase
{
    private const string _CacheName = "features";

    // A file under the test output: EnsureCreated and the repository's own contexts must reach the same database.
    private readonly string _databasePath = Path.Combine(AppContext.BaseDirectory, $"features-{Guid.NewGuid():N}.db");

    private readonly ICache _defaultCache = Substitute.For<ICache>();
    private readonly ICache _namedCache = Substitute.For<ICache>();
    private readonly ServiceProvider _services;

    public EfFeatureValueRecordRepositoryNamedCacheTests()
    {
        var cacheProvider = Substitute.For<ICacheProvider>();
        cacheProvider.GetCache(_CacheName).Returns(_namedCache);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<FeaturesDbContext>(options =>
            options.UseSqlite($"Data Source={_databasePath};Pooling=False")
        );
        services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureManagement(options => options.FeatureValueCacheName = _CacheName);
            setup.UseEntityFramework<FeaturesDbContext>();
        });
        services.AddSingleton(_defaultCache);
        services.AddSingleton(cacheProvider);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public async Task should_update_the_named_cache_the_store_reads_when_the_repository_writes()
    {
        // given
        await using (
            var db = await _services
                .GetRequiredService<IDbContextFactory<FeaturesDbContext>>()
                .CreateDbContextAsync(AbortToken)
        )
        {
            await db.Database.EnsureCreatedAsync(AbortToken);
        }

        var repository = _services.GetRequiredService<IFeatureValueRecordRepository>();
        var record = new FeatureValueRecord(Guid.NewGuid(), "Reports", "true", "Tenant", "t1");
        var key = FeatureValueCacheItem.CalculateCacheKey("Reports", "Tenant", "t1");

        // when
        await repository.InsertAsync(record, AbortToken);
        record.Value = "false";
        await repository.UpdateAsync(record, AbortToken);
        await repository.SaveAsync([], [record], [], AbortToken);
        await repository.DeleteAsync([record], AbortToken);

        // then
        await _namedCache
            .Received(4)
            .UpsertAllAsync(
                Arg.Is<IDictionary<string, FeatureValueCacheItem>>(items => items.Keys.Single() == key),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            );
        _defaultCache.ReceivedCalls().Should().BeEmpty();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _services.DisposeAsync();
        File.Delete(_databasePath);
        await base.DisposeAsyncCore();
    }

    public sealed class FeaturesDbContext(DbContextOptions<FeaturesDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessFeatures(new FeaturesStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
