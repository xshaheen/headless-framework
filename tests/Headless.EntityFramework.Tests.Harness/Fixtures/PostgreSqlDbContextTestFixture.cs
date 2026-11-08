// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Context;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing;
using Headless.Testing.Testcontainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Fixtures;

/// <summary>
/// Base test fixture for PostgreSQL-based DbContext integration tests.
/// Provides a PostgreSQL database of the fixture's own and shared test infrastructure.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public abstract class PostgreSqlDbContextTestFixture<TContext> : IDbContextTestFixture<TContext>, IAsyncLifetime
    where TContext : DbContext
{
    private static readonly Faker _Faker = new();
    private static readonly string _UserId = Guid.NewGuid().ToString();
    private static readonly DateTimeOffset _Now = _Faker.Date.RecentOffset().ToUniversalTime();

    public string UserId => _UserId;

    public DateTimeOffset Now => _Now;

    /// <summary>Gets the PostgreSQL fixture, for a test that needs a further database beside the fixture's own.</summary>
    public HeadlessPostgreSqlFixture PostgreSql { get; } = new();

    public string SqlConnectionString => PostgreSql.ConnectionString;

    public ServiceProvider ServiceProvider { get; private set; } = null!;

    public FakeTimeProvider Clock { get; } = new(_Now);

    public TestCurrentTenant CurrentTenant { get; } = new() { Id = null };

    public TestCurrentUser CurrentUser { get; } = new() { UserId = _UserId };

    public virtual async ValueTask InitializeAsync()
    {
        await ((IAsyncLifetime)PostgreSql).InitializeAsync();
        var services = CreateServiceCollection();
        ServiceProvider = services.BuildServiceProvider();
        await OnDatabaseInitializeAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        await PostgreSql.DisposeAsync();
        GC.SuppressFinalize(this);
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
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddHeadlessDbContextServices();
        services.AddRecordingHeadlessDispatcher();
        services.AddSingleton<ICurrentTenant>(CurrentTenant);

        ConfigureDbContext(services);

        return services;
    }

    /// <summary>
    /// Configures the DbContext registration. Override to customize DbContext options.
    /// </summary>
    protected abstract void ConfigureDbContext(IServiceCollection services);
}
