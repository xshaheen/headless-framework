// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Domain;
using Headless.Features;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Messaging.InMemoryStorage;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests.TestSetup;

[Collection<FeaturesTestFixture>]
public abstract class FeaturesTestBase(FeaturesTestFixture fixture) : TestBase
{
    protected FeaturesTestFixture Fixture { get; } = fixture;

    protected IHost CreateHost(Action<IHostApplicationBuilder>? configure = null)
    {
        var builder = CreateHostBuilder();
        configure?.Invoke(builder);

        return builder.Build();
    }

    protected HostApplicationBuilder CreateHostBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        ConfigureFeaturesServices(builder);
        return builder;
    }

    protected void ConfigureFeaturesServices(IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton<ICancellationTokenProvider>(DefaultCancellationTokenProvider.Instance);
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
        services.AddSingleton(Substitute.For<ICurrentPrincipalAccessor>());
        services.AddHeadlessLocalEventBus();

        // Cache
        services.AddHeadlessCaching(setup =>
            setup.UseRedis(options => options.ConnectionMultiplexer = Fixture.Multiplexer)
        );
        // Lock Storage
        services.AddSingleton<IConnectionMultiplexer>(Fixture.Multiplexer);
        // Resource Lock
        services.AddHeadlessDistributedLocks(setup => setup.UseRedis());
        // Messages
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        services.AddDbContextFactory<FeaturesTestDbContext>(options => options.UseNpgsql(Fixture.SqlConnectionString));

        services.AddHeadlessFeatures(setup =>
        {
            setup.ConfigureStorage(ConfigureFeaturesStorage);
            setup.UseEntityFramework<FeaturesTestDbContext>();
        });
    }

    protected virtual void ConfigureFeaturesStorage(FeaturesStorageOptions options) { }

    protected sealed class FeaturesTestDbContext(
        DbContextOptions<FeaturesTestDbContext> options,
        IOptions<FeaturesStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessFeatures(storageOptions.Value);
        }
    }
}
