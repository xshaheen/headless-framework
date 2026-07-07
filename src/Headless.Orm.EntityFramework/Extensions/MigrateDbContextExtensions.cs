// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

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
public static class MigrateDbContextExtensions
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
    }
}
