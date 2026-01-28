// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class MigrateDbContextExtensions
{
    extension(IServiceProvider services)
    {
        public void MigrateDbContext<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.Migrate();
        }

        public async Task MigrateDbContextAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.MigrateAsync(token).AnyContext();
        }

        public void MigrateDbContextByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.Migrate();
        }

        public async Task MigrateDbContextByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).AnyContext();
            await context.Database.MigrateAsync(token).AnyContext();
        }

        public void EnsureDbCreated<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbCreatedAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureCreatedAsync(token).AnyContext();
        }

        public void EnsureDbCreatedByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbCreatedByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).AnyContext();
            await context.Database.EnsureCreatedAsync(token).AnyContext();
        }

        public void EnsureDbDeleted<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureDeleted();
        }

        public async Task EnsureDbDeletedAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureDeletedAsync(token).AnyContext();
        }

        public void EnsureDbDeletedByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureDeleted();
        }

        public async Task EnsureDbDeletedByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).AnyContext();
            await context.Database.EnsureDeletedAsync(token).AnyContext();
        }

        public void EnsureDbRecreated<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbRecreatedAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureDeletedAsync(token).AnyContext();
            await context.Database.EnsureCreatedAsync(token).AnyContext();
        }

        public void EnsureDbRecreatedByFactory<TContext>()
            where TContext : DbContext
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbRecreatedByFactoryAsync<TContext>(CancellationToken token = default)
            where TContext : DbContext
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token).AnyContext();
            await context.Database.EnsureDeletedAsync(token).AnyContext();
            await context.Database.EnsureCreatedAsync(token).AnyContext();
        }
    }
}
