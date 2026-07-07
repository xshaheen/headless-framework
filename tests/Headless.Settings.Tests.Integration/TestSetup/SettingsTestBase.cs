using Headless;
using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Domain;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Messaging.InMemoryStorage;
using Headless.Settings;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton<ICancellationTokenProvider>(DefaultCancellationTokenProvider.Instance);
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
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
        services.AddStringEncryptionService(builder.Configuration.GetRequiredSection("Headless:StringEncryption"));

        services.AddDbContextFactory<SettingsTestDbContext>(options => options.UseNpgsql(Fixture.SqlConnectionString));

        services.AddHeadlessSettings(setup =>
        {
            setup.ConfigureStorage(ConfigureSettingsStorage);
            setup.UseEntityFramework<SettingsTestDbContext>();
        });
    }

    protected virtual void ConfigureSettingsStorage(SettingsStorageOptions options) { }

    private static void _AddDefaultStringEncryptionConfiguration(IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultPassPhrase", "TestPassPhrase123456"),
            new KeyValuePair<string, string?>("Headless:StringEncryption:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
            new KeyValuePair<string, string?>("Headless:StringEncryption:DefaultSalt", "VGVzdFNhbHQ="),
        ]);
    }

    protected sealed class SettingsTestDbContext(
        DbContextOptions<SettingsTestDbContext> options,
        IOptions<SettingsStorageOptions> storageOptions
    ) : DbContext(options)
    {
        internal SettingsStorageOptions StorageOptions => storageOptions.Value;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ReplaceService<IModelCacheKeyFactory, SettingsStorageModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessSettings(storageOptions.Value);
        }
    }

    private sealed class SettingsStorageModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            if (context is not SettingsTestDbContext settingsContext)
            {
                return (context.GetType(), designTime);
            }

            var options = settingsContext.StorageOptions;

            return (
                context.GetType(),
                options.Schema,
                options.SettingValuesTableName,
                options.SettingDefinitionsTableName,
                designTime
            );
        }
    }
}
