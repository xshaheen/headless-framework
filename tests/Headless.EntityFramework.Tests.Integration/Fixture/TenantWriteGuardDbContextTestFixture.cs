using Headless;
using Headless.Context;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fixtures;

namespace Tests.Fixture;

public abstract class TenantWriteGuardDbContextTestFixtureBase : IAsyncLifetime
{
    private static readonly Faker _Faker = new();

    private readonly HeadlessPostgreSqlFixture _postgreSql = new();

    public static string UserId { get; } = Guid.NewGuid().ToString();

    public static DateTimeOffset Now { get; } = _Faker.Date.RecentOffset().ToUniversalTime();

    protected abstract bool GuardEnabled { get; }

    public string SqlConnectionString => _postgreSql.ConnectionString;

    public ServiceProvider ServiceProvider { get; private set; } = null!;

    public FakeTimeProvider Clock { get; } = new(Now);

    public TestCurrentTenant CurrentTenant { get; } = new();

    public TestCurrentUser CurrentUser { get; } = new() { UserId = UserId };

    public async ValueTask InitializeAsync()
    {
        await ((IAsyncLifetime)_postgreSql).InitializeAsync();

        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;

        services.AddLogging(x => x.AddProvider(TestHelpers.CreateXUnitLoggerFactory().Provider));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));

        if (GuardEnabled)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        }
        else
        {
            services.AddHeadlessDbContextServices();
        }

        services.AddOrReplaceSingleton<ICurrentTenant>(_ => CurrentTenant);
        services.AddRecordingHeadlessDispatcher();

        services.AddDbContext<TestHeadlessDbContext>(options => options.UseNpgsql(SqlConnectionString));

        ServiceProvider = services.BuildServiceProvider();

        await ServiceProvider.EnsureDbRecreatedAsync<TestHeadlessDbContext>();
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        await _postgreSql.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public async Task ResetAsync()
    {
        CurrentTenant.Id = null;

        await using var scope = ServiceProvider.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();

        await db.Tests.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Basics.ExecuteDeleteAsync();
    }
}

public sealed class TenantWriteGuardEnabledFixture : TenantWriteGuardDbContextTestFixtureBase
{
    protected override bool GuardEnabled => true;
}

public sealed class TenantWriteGuardDisabledFixture : TenantWriteGuardDbContextTestFixtureBase
{
    protected override bool GuardEnabled => false;
}

[CollectionDefinition]
public sealed class TenantWriteGuardCollection
    : ICollectionFixture<TenantWriteGuardEnabledFixture>,
        ICollectionFixture<TenantWriteGuardDisabledFixture>;
