// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Hosting.Initialization;
using Headless.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.TestSetup;

namespace Tests;

/// <summary>
/// The EF settings store over a <see cref="HeadlessDbContext"/>, registered pooled and per scope: the store's
/// singleton factory is the one each Headless registration provides, and writes run through the Headless save
/// pipeline of the context's own scope.
/// </summary>
public sealed class SettingsHeadlessDbContextTests(SettingsTestFixture fixture) : SettingsTestBase(fixture)
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
        var repository = scope.ServiceProvider.GetRequiredService<ISettingValueRecordRepository>();
        var kept = new SettingValueRecord(Guid.NewGuid(), "Theme", "old", "Tenant", "t1");

        // when
        await repository.InsertAsync(kept, AbortToken);
        await repository.SaveAsync(
            [new SettingValueRecord(Guid.NewGuid(), "Locale", "ar", "Tenant", "t1")],
            [new SettingValueRecord(kept.Id, "Theme", "new", "Tenant", "t1")],
            [],
            AbortToken
        );

        // then
        var stored = await repository.GetListAsync("Tenant", "t1", AbortToken);
        stored.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo([("Theme", "new"), ("Locale", "ar")]);
        host.Services.GetRequiredService<IDbContextFactory<HeadlessSettingsDbContext>>()
            .Should()
            .Match(factory => (factory is PooledDbContextFactory<HeadlessSettingsDbContext>) == pooled);
    }

    protected override void AddSettingsDbContextFactory(IServiceCollection services)
    {
        if (_pooled)
        {
            services.AddHeadlessDbContextPool<HeadlessSettingsDbContext>(options =>
                options.UseNpgsql(Fixture.SqlConnectionString)
            );
        }
        else
        {
            services.AddHeadlessDbContext<HeadlessSettingsDbContext>(options =>
                options.UseNpgsql(Fixture.SqlConnectionString)
            );
        }
    }

    protected override void UseSettingsEntityFramework(HeadlessSettingsSetupBuilder setup)
    {
        setup.UseEntityFramework<HeadlessSettingsDbContext>();
    }

    private sealed class HeadlessSettingsDbContext(
        DbContextOptions<HeadlessSettingsDbContext> options,
        IOptions<SettingsStorageOptions> storageOptions
    ) : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessSettings(
                storageOptions.Value,
                HeadlessStorageNaming.ForProvider(Database.ProviderName)
            );
        }
    }
}
