// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Hosting.Seeders;

/// <summary>
/// An <c>ISeeder</c> that applies pending EF Core migrations for <typeparamref name="TDbContext"/> at
/// application startup. Runs at the lowest seeder priority (<c>int.MinValue</c>) so migrations always
/// execute before any data-seed seeders.
/// </summary>
/// <remarks>
/// The seeder first tries to resolve <typeparamref name="TDbContext"/> directly; if it is not registered
/// it falls back to <c>IDbContextFactory&lt;TDbContext&gt;</c>. An
/// <see cref="InvalidOperationException"/> is thrown when neither is registered.
/// </remarks>
[PublicAPI]
[SeederPriority(int.MinValue)]
public sealed class DbMigrationSeeder<TDbContext>(IServiceProvider provider) : ISeeder
    where TDbContext : DbContext
{
    /// <summary>Applies pending migrations for <typeparamref name="TDbContext"/>.</summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <exception cref="InvalidOperationException">
    /// Neither <typeparamref name="TDbContext"/> nor its factory are registered.
    /// </exception>
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
