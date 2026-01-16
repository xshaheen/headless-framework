using Framework.Abstractions;
using Framework.Orm.EntityFramework;
using Framework.Testing.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Tests.Fixture;

[CollectionDefinition(DisableParallelization = true)]
public sealed class HeadlessDbContextTestFixture : ICollectionFixture<HeadlessDbContextTestFixture>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = _CreatePostgreSqlContainer();

    public static Faker Faker { get; } = new();

    public static string UserId { get; } = Guid.NewGuid().ToString();

    public static DateTimeOffset Now { get; } = Faker.Date.RecentOffset().ToUniversalTime();

    public string SqlConnectionString => _postgreSqlContainer.GetConnectionString();

    public ServiceProvider ServiceProvider { get; private set; } = null!;

    public TestClock Clock { get; } = new() { TimeProvider = new FakeTimeProvider(Now) };

    public TestCurrentTenant CurrentTenant { get; } = new() { Id = null };

    public TestCurrentUser CurrentUser { get; } = new() { UserId = UserId };

    public async ValueTask InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        var services = _CreateServiceCollection();
        ServiceProvider = services.BuildServiceProvider();
        await ServiceProvider.EnsureDbRecreatedAsync<TestHeadlessDbContext>();
    }

    public async ValueTask DisposeAsync()
    {
        await _postgreSqlContainer.StopAsync();
        await _postgreSqlContainer.DisposeAsync();
        await ServiceProvider.DisposeAsync();
    }

    private static PostgreSqlContainer _CreatePostgreSqlContainer()
    {
        return new PostgreSqlBuilder("postgres:18.1-alpine3.23")
            .WithLabel("type", "permissions")
            .WithDatabase("framework_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }

    private ServiceCollection _CreateServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddLogging(x => x.AddProvider(TestHelpers.CreateXUnitLoggerFactory().Provider));
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentTenant>(CurrentTenant);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        services.AddHeadlessDbContextServices();

        services.AddDbContext<TestHeadlessDbContext>(options =>
            options.UseNpgsql(SqlConnectionString).AddHeadlessExtension()
        );

        return services;
    }
}
