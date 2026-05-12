using Headless.Abstractions;
using Headless.EntityFramework;
using Headless.Testing.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fixtures;

namespace Tests.Fixture;

public sealed class TenantWriteGuardDbContextTestFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TenantWriteGuardDbContextTestFixture(bool guardEnabled)
    {
        _connection.Open();

        var services = new ServiceCollection();

        services.AddLogging(x => x.AddProvider(TestHelpers.CreateXUnitLoggerFactory().Provider));
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();

        if (guardEnabled)
        {
            services.AddHeadlessTenantWriteGuard();
        }
        else
        {
            services.AddHeadlessDbContextServices();
        }

        services.AddOrReplaceSingleton<ICurrentTenant>(_ => CurrentTenant);
        services.AddHeadlessMessageDispatcher<RecordingHeadlessMessageDispatcher>();

        services.AddDbContext<TestHeadlessDbContext>(options => options.UseSqlite(_connection).AddHeadlessExtension());

        ServiceProvider = services.BuildServiceProvider();

        using var scope = ServiceProvider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<TestHeadlessDbContext>();
        db.Database.EnsureCreated();
    }

    public static string UserId { get; } = Guid.NewGuid().ToString();

    public static DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

    public ServiceProvider ServiceProvider { get; }

    public TestClock Clock { get; } = new() { TimeProvider = new FakeTimeProvider(Now) };

    public TestCurrentTenant CurrentTenant { get; } = new();

    public TestCurrentUser CurrentUser { get; } = new() { UserId = UserId };

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
