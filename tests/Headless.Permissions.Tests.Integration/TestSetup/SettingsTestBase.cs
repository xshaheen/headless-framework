using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Domain;
using Headless.Permissions;
using Headless.Permissions.Seeders;
using Headless.Permissions.Storage.EntityFramework;
using Headless.Redis;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests.TestSetup;

[Collection<PermissionsTestFixture>]
public abstract class PermissionsTestBase(PermissionsTestFixture fixture) : TestBase
{
    public PermissionsTestFixture Fixture { get; } = fixture;

    protected IHost CreateHost(Action<IHostApplicationBuilder>? configure = null)
    {
        var builder = CreateHostBuilder();
        configure?.Invoke(builder);

        return builder.Build();
    }

    protected HostApplicationBuilder CreateHostBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        ConfigurePermissionsServices(builder);

        return builder;
    }

    protected void ConfigurePermissionsServices(IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        services.AddSingleton<ICancellationTokenProvider>(DefaultCancellationTokenProvider.Instance);
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
        services.AddSingleton(Substitute.For<ICurrentPrincipalAccessor>());
        services.AddSingleton(Substitute.For<IDistributedMessagePublisher>());
        services.AddServiceProviderLocalMessagePublisher();

        // Messages
        services.AddMessages(options =>
        {
            options.UseInMemoryMessageQueue();
            options.UseInMemoryStorage();
        });
        // Cache
        services.AddRedisCache(options => options.ConnectionMultiplexer = Fixture.Multiplexer);
        // Lock Storage
        services.AddSingleton<IConnectionMultiplexer>(Fixture.Multiplexer);
        services.AddSingleton<HeadlessRedisScriptsLoader>();
        // Resource Lock
        services.AddDistributedLock<RedisDistributedLockStorage>();

        services
            .AddPermissionsManagementCore()
            .AddPermissionsManagementDbContextStorage(options => options.UseNpgsql(Fixture.SqlConnectionString));

        services.RemoveHostedService<PermissionsInitializationBackgroundService>();
    }
}
