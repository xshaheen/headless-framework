// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

[PublicAPI]
public static class MigrateDbContextExtensions
{
    extension<TContext>(IServiceProvider services) where TContext : DbContext
    {
        public void MigrateDbContext()
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.Migrate();
        }

        public async Task MigrateDbContextAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.MigrateAsync(token);
        }

        public void MigrateDbContextByFactory()
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.Migrate();
        }

        public async Task MigrateDbContextByFactoryAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token);
            await context.Database.MigrateAsync(token);
        }

        public void EnsureDbCreated()
        {
            using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbCreatedAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureCreatedAsync(token);
        }

        public void EnsureDbCreatedByFactory()
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbCreatedByFactoryAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token);
            await context.Database.EnsureCreatedAsync(token);
        }

        public void EnsureDbDeleted()
        {
            using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureDeleted();
        }

        public async Task EnsureDbDeletedAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureDeletedAsync(token);
        }

        public void EnsureDbDeletedByFactory()
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureDeleted();
        }

        public async Task EnsureDbDeletedByFactoryAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token);
            await context.Database.EnsureDeletedAsync(token);
        }

        public void EnsureDbRecreated()
        {
            using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbRecreatedAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.EnsureDeletedAsync(token);
            await context.Database.EnsureCreatedAsync(token);
        }

        public void EnsureDbRecreatedByFactory()
        {
            using var scope = services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            using var context = factory.CreateDbContext();
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }

        public async Task EnsureDbRecreatedByFactoryAsync(CancellationToken token = default)
        {
            await using var scope = services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync(token);
            await context.Database.EnsureDeletedAsync(token);
            await context.Database.EnsureCreatedAsync(token);
        }
    }
}
