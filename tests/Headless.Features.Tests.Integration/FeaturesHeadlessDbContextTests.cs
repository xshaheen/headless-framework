// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Features;
using Headless.Features.Entities;
using Headless.Features.Repositories;
using Headless.Hosting.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

/// <summary>
/// The EF features store over a <see cref="HeadlessDbContext"/>, registered pooled and per scope: the store's
/// singleton factory is the one each Headless registration provides, and writes run through the Headless save
/// pipeline of the context's own scope.
/// </summary>
public sealed class FeaturesHeadlessDbContextTests(FeaturesTestFixture fixture) : FeaturesTestBase(fixture)
{
    private bool _pooled;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_store_and_read_values_through_a_headless_db_context(bool pooled)
    {
        // given
        _pooled = pooled;
        await Fixture.ResetAsync();
        using var host = CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IFeatureValueRecordRepository>();
        var kept = new FeatureValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");

        // when
        await repository.InsertAsync(kept, AbortToken);
        await repository.SaveAsync(
            [new FeatureValueRecord(Guid.NewGuid(), "Locale", "ar", "Tenant", "t1")],
            [new FeatureValueRecord(kept.Id, "Theme", "new", "Tenant", "t1")],
            [],
            AbortToken
        );

        // then
        var stored = await repository.GetListAsync(["Theme", "Locale"], "Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "new"), ("Locale", "ar")]);
        host.Services.GetRequiredService<IDbContextFactory<HeadlessFeaturesDbContext>>()
            .Should()
            .Match(factory => (factory is PooledDbContextFactory<HeadlessFeaturesDbContext>) == pooled);
    }

    protected override void AddFeaturesDbContextFactory(IServiceCollection services)
    {
        if (_pooled)
        {
            services.AddHeadlessDbContextPool<HeadlessFeaturesDbContext>(options =>
                options.UseNpgsql(Fixture.SqlConnectionString)
            );
        }
        else
        {
            services.AddHeadlessDbContext<HeadlessFeaturesDbContext>(options =>
                options.UseNpgsql(Fixture.SqlConnectionString)
            );
        }
    }

    protected override void UseFeaturesEntityFramework(HeadlessFeaturesSetupBuilder setup)
    {
        setup.UseEntityFramework<HeadlessFeaturesDbContext>();
    }

    private sealed class HeadlessFeaturesDbContext(
        DbContextOptions<HeadlessFeaturesDbContext> options,
        IOptions<FeaturesStorageOptions> storageOptions
    ) : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessFeatures(
                storageOptions.Value,
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
        }
    }
}
