// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.Hosting.Seeders;

[PublicAPI]
[SeederPriority(int.MinValue)]
public sealed class DbMigrationPreSeeder<TDbContext>(IServiceProvider provider) : IPreSeeder
    where TDbContext : DbContext
{
    public async ValueTask SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = provider.CreateAsyncScope();

        // Try with DbContext
        var dbContext = scope.ServiceProvider.GetService<TDbContext>();

        if (dbContext is not null)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);

            return;
        }

        // Try with DbContextFactory
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TDbContext>>();

        if (factory is not null)
        {
            await using var createdDbContext = await factory.CreateDbContextAsync(cancellationToken);
            await createdDbContext.Database.MigrateAsync(cancellationToken);

            return;
        }

        throw new InvalidOperationException(
            $"Unable to find {typeof(TDbContext).Name} or {typeof(IDbContextFactory<TDbContext>).Name} in the service provider."
        );
    }
}
