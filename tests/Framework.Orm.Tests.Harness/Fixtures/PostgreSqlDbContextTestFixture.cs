// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Orm.EntityFramework;
using Framework.Testing.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace Tests.Fixtures;

/// <summary>
/// Base test fixture for PostgreSQL-based DbContext integration tests.
/// Provides PostgreSQL container setup and shared test infrastructure.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public abstract class PostgreSqlDbContextTestFixture<TContext> : IDbContextTestFixture<TContext>, IAsyncLifetime
    where TContext : DbContext
{
    private static readonly Faker _faker = new();
    private static readonly string _userId = Guid.NewGuid().ToString();
    private static readonly DateTimeOffset _now = _faker.Date.RecentOffset().ToUniversalTime();

    private readonly PostgreSqlContainer _postgreSqlContainer = _CreatePostgreSqlContainer();

    public string UserId => _userId;

    public DateTimeOffset Now => _now;

    public string SqlConnectionString => _postgreSqlContainer.GetConnectionString();

    public ServiceProvider ServiceProvider { get; private set; } = null!;

    public TestClock Clock { get; } = new() { TimeProvider = new FakeTimeProvider(_now) };

    public TestCurrentTenant CurrentTenant { get; } = new() { Id = null };

    public TestCurrentUser CurrentUser { get; } = new() { UserId = _userId };

    public virtual async ValueTask InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
        var services = CreateServiceCollection();
        ServiceProvider = services.BuildServiceProvider();
        await OnDatabaseInitializeAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await _postgreSqlContainer.StopAsync();
        await _postgreSqlContainer.DisposeAsync();
        await ServiceProvider.DisposeAsync();
    }

    /// <summary>
    /// Called after the database container is started. Override to perform database initialization.
    /// Default implementation calls EnsureDbRecreatedAsync.
    /// </summary>
    protected virtual async Task OnDatabaseInitializeAsync()
    {
        await ServiceProvider.EnsureDbRecreatedAsync<TContext>();
    }

    public TContext CreateContext(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<TContext>();
    }

    /// <summary>
    /// Creates the service collection for tests. Override to customize service registration.
    /// </summary>
    protected virtual ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddLogging(x => x.AddProvider(TestHelpers.CreateXUnitLoggerFactory().Provider));
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentTenant>(CurrentTenant);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        services.AddHeadlessDbContextServices();

        ConfigureDbContext(services);

        return services;
    }

    /// <summary>
    /// Configures the DbContext registration. Override to customize DbContext options.
    /// </summary>
    protected abstract void ConfigureDbContext(IServiceCollection services);

    private static PostgreSqlContainer _CreatePostgreSqlContainer()
    {
        return new PostgreSqlBuilder("postgres:18.1-alpine3.23")
            .WithLabel("type", "orm-harness")
            .WithDatabase("framework_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }
}
