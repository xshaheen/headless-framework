// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

public sealed class TenantWriteGuardDbContextTestFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgreSqlContainer;

    private TenantWriteGuardDbContextTestFixture(bool guardEnabled)
    {
        GuardEnabled = guardEnabled;
        _postgreSqlContainer = _CreatePostgreSqlContainer();
    }

    public static string UserId { get; } = Guid.NewGuid().ToString();

    public static DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

    public bool GuardEnabled { get; }

    public string SqlConnectionString => _postgreSqlContainer.GetConnectionString();

    public ServiceProvider ServiceProvider { get; private set; } = null!;

    public TestClock Clock { get; } = new() { TimeProvider = new FakeTimeProvider(Now) };

    public TestCurrentTenant CurrentTenant { get; } = new();

    public TestCurrentUser CurrentUser { get; } = new() { UserId = UserId };

    public static async Task<TenantWriteGuardDbContextTestFixture> CreateAsync(bool guardEnabled)
    {
        var fixture = new TenantWriteGuardDbContextTestFixture(guardEnabled);

        try
        {
            await fixture._InitializeAsync();
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }

        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await ServiceProvider.DisposeAsync();
        }

        if (_postgreSqlContainer is not null)
        {
            await _postgreSqlContainer.StopAsync();
            await _postgreSqlContainer.DisposeAsync();
        }
    }

    private async Task _InitializeAsync()
    {
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

    private static PostgreSqlContainer _CreatePostgreSqlContainer()
    {
        return new PostgreSqlBuilder(TestImages.PostgreSql)
            .WithLabel("type", "tenant-write-guard")
            .WithDatabase("headless_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }
}
