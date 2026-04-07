using Headless;
using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Domain;
using Headless.Redis;
using Headless.Settings;
using Headless.Settings.Storage.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests.TestSetup;

[Collection<SettingsTestFixture>]
public abstract class SettingsTestBase(SettingsTestFixture fixture) : TestBase
{
    protected SettingsTestFixture Fixture { get; } = fixture;

    protected IHost CreateHost(Action<IHostApplicationBuilder>? configure = null)
    {
        var builder = CreateHostBuilder();
        configure?.Invoke(builder);

        return builder.Build();
    }

    protected HostApplicationBuilder CreateHostBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        ConfigureSettingsServices(builder);
        return builder;
    }

    protected void ConfigureSettingsServices(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        _AddDefaultStringEncryptionConfiguration(builder.Configuration);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        services.AddSingleton<ICancellationTokenProvider>(DefaultCancellationTokenProvider.Instance);
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
        services.AddServiceProviderLocalMessagePublisher();

        // Messages
        services.AddHeadlessMessaging(options =>
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
        services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

        services
            .AddSettingsManagementCore()
            .AddSettingsManagementDbContextStorage(options => options.UseNpgsql(Fixture.SqlConnectionString));
    }

    private static void _AddDefaultStringEncryptionConfiguration(IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
        ]);
    }
}
