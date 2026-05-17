using Headless.Abstractions;
using Headless.EntityFramework;
using Headless.Testing.Helpers;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;
using Tests.Fixtures;

namespace Tests.Fixture;

public abstract class TenantWriteGuardDbContextTestFixtureBase : IAsyncLifetime
{
    private static readonly Faker _Faker = new();

    private PostgreSqlContainer _postgreSqlContainer = null!;

    public static string UserId { get; } = Guid.NewGuid().ToString();

    public static DateTimeOffset Now { get; } = _Faker.Date.RecentOffset().ToUniversalTime();

    protected abstract bool GuardEnabled { get; }

    protected abstract string ContainerLabel { get; }

    public string SqlConnectionString => _postgreSqlContainer.GetConnectionString();

    public ServiceProvider ServiceProvider { get; private set; } = null!;

    public TestClock Clock { get; } = new() { TimeProvider = new FakeTimeProvider(Now) };

    public TestCurrentTenant CurrentTenant { get; } = new();

    public TestCurrentUser CurrentUser { get; } = new() { UserId = UserId };

    public async ValueTask InitializeAsync()
    {
        _postgreSqlContainer = _CreatePostgreSqlContainer(ContainerLabel);
        await _postgreSqlContainer.StartAsync();

        var services = new ServiceCollection();

        services.AddLogging(x => x.AddProvider(TestHelpers.CreateXUnitLoggerFactory().Provider));
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();

        if (GuardEnabled)
        {
            services.AddHeadlessTenantWriteGuard();
        }
        else
        {
            services.AddHeadlessDbContextServices();
        }

        services.AddOrReplaceSingleton<ICurrentTenant>(_ => CurrentTenant);
        services.AddHeadlessMessageDispatcher<RecordingHeadlessMessageDispatcher>();

        services.AddDbContext<TestHeadlessDbContext>(options =>
            options.UseNpgsql(SqlConnectionString).AddHeadlessExtension()
        );

        ServiceProvider = services.BuildServiceProvider();

        await ServiceProvider.EnsureDbRecreatedAsync<TestHeadlessDbContext>();
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        await _postgreSqlContainer.DisposeAsync();
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

    private static PostgreSqlContainer _CreatePostgreSqlContainer(string label)
    {
        return new PostgreSqlBuilder(TestImages.PostgreSql)
            .WithLabel("type", label)
            .WithDatabase("headless_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithReuse(true)
            .Build();
    }
}

public sealed class TenantWriteGuardEnabledFixture : TenantWriteGuardDbContextTestFixtureBase
{
    protected override bool GuardEnabled => true;

    protected override string ContainerLabel => "tenant-write-guard-enabled";
}

public sealed class TenantWriteGuardDisabledFixture : TenantWriteGuardDbContextTestFixtureBase
{
    protected override bool GuardEnabled => false;

    protected override string ContainerLabel => "tenant-write-guard-disabled";
}

[CollectionDefinition(DisableParallelization = true)]
public sealed class TenantWriteGuardCollection
    : ICollectionFixture<TenantWriteGuardEnabledFixture>,
        ICollectionFixture<TenantWriteGuardDisabledFixture>;
