// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension members on <see cref="IServiceProvider"/> for managing the database lifecycle
/// (migrate, ensure created, ensure deleted, recreate) by resolving the context or its factory from
/// the service provider.
/// </summary>
/// <remarks>
/// Each method creates and disposes its own service scope, making them safe to call from the
/// application startup path (before the first request scope exists). The <c>ByFactory</c> variants
/// resolve the context through <c>IDbContextFactory</c> rather than directly, which is useful when
/// the context is not registered in DI or the factory lifetime differs.
/// </remarks>
[PublicAPI]
public static class HeadlessMigrateDbContextExtensions
{
    extension(IServiceProvider services)
    {
        /// <summary>
        /// Applies all pending EF Core migrations for <typeparamref name="TContext"/> by resolving it from a
        /// new service scope.
        /// </summary>
        public void MigrateDbContext<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.Migrate();
        }

        /// <summary>
        /// Asynchronously applies all pending EF Core migrations for <typeparamref name="TContext"/>.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        public async Task MigrateDbContextAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.MigrateAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies all pending EF Core migrations for <typeparamref name="TContext"/> using the registered
        /// <c>IDbContextFactory</c>.
        /// </summary>
        public void MigrateDbContextByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.Migrate();
        }

        /// <summary>
        /// Asynchronously applies all pending EF Core migrations using the registered <c>IDbContextFactory</c>.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        public async Task MigrateDbContextByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).ConfigureAwait(false);
            await context.Database.MigrateAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the database for <typeparamref name="TContext"/> if it does not already exist.
        /// Does not apply migrations; use <c>MigrateDbContext</c> for migration-based schemas.
        /// </summary>
        public void EnsureDbCreated<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureCreated();
        }

        /// <summary>Asynchronously creates the database if it does not already exist.</summary>
        /// <param name="token">A cancellation token.</param>
        public async Task EnsureDbCreatedAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureCreatedAsync(token).ConfigureAwait(false);
        }

        /// <summary>Creates the database if it does not already exist, using the registered factory.</summary>
        public void EnsureDbCreatedByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureCreated();
        }

        /// <summary>Asynchronously creates the database if it does not already exist, using the registered factory.</summary>
        /// <param name="token">A cancellation token.</param>
        public async Task EnsureDbCreatedByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).ConfigureAwait(false);
            await context.Database.EnsureCreatedAsync(token).ConfigureAwait(false);
        }

        /// <summary>Deletes the database for <typeparamref name="TContext"/> if it exists.</summary>
        public void EnsureDbDeleted<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureDeleted();
        }

        /// <summary>Asynchronously deletes the database if it exists.</summary>
        /// <param name="token">A cancellation token.</param>
        public async Task EnsureDbDeletedAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureDeletedAsync(token).ConfigureAwait(false);
        }

        /// <summary>Deletes the database if it exists, using the registered factory.</summary>
        public void EnsureDbDeletedByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureDeleted();
        }

        /// <summary>Asynchronously deletes the database if it exists, using the registered factory.</summary>
        /// <param name="token">A cancellation token.</param>
        public async Task EnsureDbDeletedByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).ConfigureAwait(false);
            await context.Database.EnsureDeletedAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the database if it exists and then creates a fresh one. Intended for test or dev
        /// scenarios; do not call in production.
        /// </summary>
        public void EnsureDbRecreated<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }

        /// <summary>
        /// Asynchronously drops the database if it exists and then creates a fresh one.
        /// </summary>
        /// <param name="token">A cancellation token.</param>
        public async Task EnsureDbRecreatedAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureDeletedAsync(token).ConfigureAwait(false);
            await context.Database.EnsureCreatedAsync(token).ConfigureAwait(false);
        }

        /// <summary>Drops and recreates the database using the registered factory.</summary>
        public void EnsureDbRecreatedByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }

        /// <summary>Asynchronously drops and recreates the database using the registered factory.</summary>
        /// <param name="token">A cancellation token.</param>
        public async Task EnsureDbRecreatedByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).ConfigureAwait(false);
            await context.Database.EnsureDeletedAsync(token).ConfigureAwait(false);
            await context.Database.EnsureCreatedAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Applies pending EF Core migrations for the tenant-routed <typeparamref name="TContext"/> to every tenant in
        /// the catalog, one tenant at a time: each tenant's schema or database gets its own migrations history.
        /// Disabled tenants are migrated too, so a re-enabled tenant is current. The context's own (host) placement is
        /// not migrated here; use <c>MigrateDbContextByFactoryAsync</c> for it.
        /// </summary>
        /// <remarks>
        /// Runs outside any request scope, so it can be called from application startup or a deployment job. The
        /// first failing tenant is logged with its id and its exception propagates unchanged; later tenants are not
        /// attempted.
        /// </remarks>
        /// <typeparam name="TContext">A context registered with <c>RouteTenantData&lt;TContext&gt;()</c>.</typeparam>
        /// <param name="token">A cancellation token.</param>
        /// <exception cref="InvalidOperationException">
        /// The configured tenant store does not implement <see cref="ITenantDirectory"/>, or a tenant has no data placement.
        /// </exception>
        public async Task MigrateTenantDatabasesAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            var directory =
                services.GetService<ITenantDirectory>()
                ?? throw new InvalidOperationException(
                    "Migrating every tenant needs the tenant store to list its tenants (ITenantDirectory). The "
                        + "configured tenant catalog store does not implement it."
                );
            var currentTenant = services.GetRequiredService<ICurrentTenant>();
            var factory = services.GetRequiredService<IDbContextFactory<TContext>>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TContext).FullName!);

            foreach (var tenant in await directory.GetAllAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();

                using var _ = currentTenant.Change(tenant.Id, tenant.Name);

                try
                {
                    await using var context = await factory.CreateDbContextAsync(token).ConfigureAwait(false);
                    await context.Database.MigrateAsync(token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogTenantMigrationFailed(e, tenant.Id);

                    throw;
                }
            }
        }
    }
}

internal static partial class HeadlessTenantMigrationLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "TenantMigrationFailed",
        Level = LogLevel.Error,
        Message = "Applying migrations for tenant {TenantId} failed; remaining tenants were not migrated."
    )]
    public static partial void LogTenantMigrationFailed(this ILogger logger, Exception exception, string tenantId);
}
