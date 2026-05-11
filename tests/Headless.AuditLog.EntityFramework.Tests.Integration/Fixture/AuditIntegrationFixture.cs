using Headless.Abstractions;
using Headless.AuditLog;
using Headless.EntityFramework;
using Headless.Testing.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Fixture;

/// <summary>
/// Per-test helper: builds an isolated ServiceProvider backed by an in-memory SQLite database.
/// Each call to <see cref="CreateAsync"/> returns a fresh, isolated scope.
/// </summary>
public static class AuditIntegrationFixture
{
    public static readonly string UserId = "user-123";
    public static readonly string TenantId = "tenant-abc";
    public static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public static Task<(ServiceProvider Sp, SqliteConnection Conn)> CreateAsync(
        Action<AuditLogOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null
    ) => CreateAsync<AuditTestDbContext>(configure, configureServices);

    /// <summary>
    /// Builds an isolated <see cref="ServiceProvider"/> with the supplied DbContext. The optional
    /// <paramref name="configureServices"/> hook runs AFTER the framework defaults are registered,
    /// so tests can swap an audit collaborator (e.g. a throwing <see cref="IAuditChangeCapture"/>).
    /// </summary>
    public static async Task<(ServiceProvider Sp, SqliteConnection Conn)> CreateAsync<TContext>(
        Action<AuditLogOptions>? configure,
        Action<IServiceCollection>? configureServices
    )
        where TContext : DbContext
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var clock = new TestClock { TimeProvider = new FakeTimeProvider(Now) };
        var currentTenant = new TestCurrentTenant { Id = TenantId };
        var currentUser = new TestCurrentUser { UserId = new Headless.Primitives.UserId(UserId) };

        var services = new ServiceCollection();

        // Register logging and all Headless infrastructure defaults first
        services.AddLogging();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICurrentTenant>(currentTenant);
        services.AddSingleton<ICurrentUser>(currentUser);
        services.AddSingleton<IGuidGenerator, SequentialAsStringGuidGenerator>();
        services.AddHeadlessDbContextServices();

        // Audit services
        services.AddHeadlessAuditLog(configure);
        services.AddAuditLogEntityFramework<TContext>();

        // Keep connection alive with the provider lifetime
        services.AddSingleton(connection);

        services.AddDbContext<TContext>(opts =>
        {
            opts.UseSqlite(connection).AddHeadlessExtension();

            // Audit capture resolves IClock, ICurrentUser, ICurrentTenant
            // via this.GetService<T>() which hits EF's INTERNAL service provider (populated by
            // IDbContextOptionsExtension.ApplyServices), not the application DI scope.
            // This extension injects the test doubles into EF's internal provider so they win
            // over the defaults (NullCurrentUser etc.) registered by AddHeadlessDbContextServices.
            ((IDbContextOptionsBuilderInfrastructure)opts).AddOrUpdateExtension(
                new TestHeadlessServicesOptionsExtension(clock, currentUser, currentTenant)
            );
        });

        // Run consumer-supplied overrides last so they can swap any default registration.
        configureServices?.Invoke(services);

        var sp = services.BuildServiceProvider();

        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await db.Database.EnsureCreatedAsync();

        return (sp, connection);
    }
}
