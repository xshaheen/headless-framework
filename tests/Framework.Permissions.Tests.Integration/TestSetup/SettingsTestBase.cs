using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Caching;
using Framework.Messaging;
using Framework.Messaging.LocalServiceProvider;
using Framework.Permissions;
using Framework.Permissions.Seeders;
using Framework.Redis;
using Framework.ResourceLocks;
using Framework.ResourceLocks.Redis;
using Framework.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using IFoundatioMessageBus = Foundatio.Messaging.IMessageBus;

namespace Tests.TestSetup;

[Collection(nameof(PermissionsTestFixture))]
public abstract class PermissionsTestBase(PermissionsTestFixture fixture, ITestOutputHelper output) : TestBase(output)
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

        // MessageBus
        services.AddSingleton<IFoundatioMessageBus>(_ => new RedisMessageBus(o =>
            o.Subscriber(Fixture.Multiplexer.GetSubscriber()).Topic("test-lock")
        ));

        services.AddMessageBusFoundatioAdapter();
        // Cache
        services.AddRedisCache(options => options.ConnectionMultiplexer = Fixture.Multiplexer);
        // Lock Storage
        services.AddSingleton<IConnectionMultiplexer>(Fixture.Multiplexer);
        services.AddSingleton<HeadlessRedisScriptsLoader>();
        // Resource Lock
        services.AddResourceLock<RedisResourceLockStorage>();

        services
            .AddPermissionsManagementCore()
            .AddPermissionsManagementDbContextStorage(options => options.UseNpgsql(Fixture.SqlConnectionString));

        services.RemoveHostedService<PermissionsInitializationBackgroundService>();
    }
}
