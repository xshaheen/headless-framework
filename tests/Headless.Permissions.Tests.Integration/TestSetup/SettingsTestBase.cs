using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Domain;
using Headless.Messaging;
using Headless.Permissions;
using Headless.Redis;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        services.AddSingleton(Substitute.For<IDirectPublisher>());
        services.AddServiceProviderLocalMessagePublisher();

        // Messages
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemoryMessageQueue();
            setup.UseInMemoryStorage();
        });
        // Cache
        services.AddRedisCache(options => options.ConnectionMultiplexer = Fixture.Multiplexer);
        // Lock Storage
        services.AddSingleton<IConnectionMultiplexer>(Fixture.Multiplexer);
        services.AddSingleton<HeadlessRedisScriptsLoader>();
        // Resource Lock
        services.AddDistributedLock<RedisDistributedLockStorage>(static _ => { });

        services.AddDbContextFactory<PermissionsTestDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );

        services.AddPermissionsManagementCore().AddHeadlessPermissions(setup =>
        {
            setup.ConfigureStorage(ConfigurePermissionsStorage);
            setup.UseEntityFramework<PermissionsTestDbContext>();
        });
    }

    protected virtual void ConfigurePermissionsStorage(PermissionsStorageOptions options) { }

    protected sealed class PermissionsTestDbContext(
        DbContextOptions<PermissionsTestDbContext> options,
        IOptions<PermissionsStorageOptions> storageOptions
    ) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessPermissions(storageOptions.Value);
        }
    }
}
