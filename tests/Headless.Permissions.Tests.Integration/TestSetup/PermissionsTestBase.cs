using Headless.Abstractions;
using Headless.Caching;
using Headless.DistributedLocks;
using Headless.DistributedLocks.Redis;
using Headless.Domain;
using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Messaging.InMemoryStorage;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton<ICancellationTokenProvider>(DefaultCancellationTokenProvider.Instance);
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton(Substitute.For<IApplicationInformationAccessor>());
        services.AddSingleton(Substitute.For<ICurrentPrincipalAccessor>());
        services.AddSingleton(Substitute.For<IBus>());
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

        services.AddDbContextFactory<PermissionsTestDbContext>(options =>
            options.UseNpgsql(Fixture.SqlConnectionString)
        );

        services.AddHeadlessPermissions(setup =>
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
        internal PermissionsStorageOptions StorageOptions => storageOptions.Value;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ReplaceService<IModelCacheKeyFactory, PermissionsStorageModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddHeadlessPermissions(storageOptions.Value);
        }
    }

    private sealed class PermissionsStorageModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            if (context is not PermissionsTestDbContext permissionsContext)
            {
                return (context.GetType(), designTime);
            }

            var options = permissionsContext.StorageOptions;

            return (
                context.GetType(),
                options.Schema,
                options.PermissionGrantsTableName,
                options.PermissionDefinitionsTableName,
                options.PermissionGroupDefinitionsTableName,
                designTime
            );
        }
    }
}
