// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Hosting.Seeders;

public sealed class DbMigrationSeeder<TDbContext>(IServiceProvider provider) : ISeeder
    where TDbContext : DbContext
{
    public async ValueTask SeedAsync()
    {
        await using var scope = provider.CreateAsyncScope();

        // Try with DbContext
        var dbContext = scope.ServiceProvider.GetService<TDbContext>();

        if (dbContext is not null)
        {
            await dbContext.Database.MigrateAsync();

            return;
        }

        // Try with DbContextFactory
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TDbContext>>();

        if (factory is not null)
        {
            await using var createdDbContext = await factory.CreateDbContextAsync();
            await createdDbContext.Database.MigrateAsync();

            return;
        }

        throw new InvalidOperationException(
            $"Unable to find {typeof(TDbContext).Name} or {typeof(IDbContextFactory<TDbContext>).Name} in the service provider."
        );
    }
}
