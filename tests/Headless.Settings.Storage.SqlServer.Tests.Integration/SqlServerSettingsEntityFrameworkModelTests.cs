// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

public sealed class SqlServerSettingsEntityFrameworkModelTests : TestBase
{
    [Fact]
    public async Task should_accept_automatic_mapping_and_emit_binary_collation_for_setting_provider_keys()
    {
        using var host = _CreateHost<SettingsDbContext>();

        await host.StartAsync(AbortToken);
        var factory = host.Services.GetRequiredService<IDbContextFactory<SettingsDbContext>>();
        await using var context = await factory.CreateDbContextAsync(AbortToken);

        var script = context.Database.GenerateCreateScript();

        script.Should().Contain("[ProviderKey] nvarchar(64) COLLATE Latin1_General_100_BIN2 NULL");
    }

    [Fact]
    public async Task should_reject_manual_mapping_without_binary_collation_at_startup()
    {
        using var host = _CreateHost<ManualSettingsDbContext>();

        var start = () => host.StartAsync(AbortToken);

        await start
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Latin1_General_100_BIN2*SettingValueRecord.ProviderKey*ConfigureHeadlessSettings(this)*");
    }

    [Fact]
    public async Task should_accept_manual_mapping_that_inherits_the_required_model_collation()
    {
        using var host = _CreateHost<BinaryDefaultSettingsDbContext>();

        await host.StartAsync(AbortToken);
    }

    private static IHost _CreateHost<TContext>()
        where TContext : DbContext
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
        builder.Services.AddHeadlessSettings(setup => setup.UseEntityFramework<TContext>());
        builder.Services.AddDbContextFactory<TContext>(options =>
            options.UseSqlServer("Server=localhost;Database=SettingsModel")
        );

        return builder.Build();
    }

    private sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessSettings(this);
        }
    }

    private sealed class ManualSettingsDbContext(DbContextOptions<ManualSettingsDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureHeadlessSettings(new SettingsStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }

    private sealed class BinaryDefaultSettingsDbContext(DbContextOptions<BinaryDefaultSettingsDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseCollation("Latin1_General_100_BIN2");
            modelBuilder.ConfigureHeadlessSettings(new SettingsStorageOptions(), StorageNamingStyle.PascalCase);
        }
    }
}
