// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework;

/// <summary>Registers EF Core migrations as a provisioned resource.</summary>
[PublicAPI]
public static class HeadlessDbContextMigrationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the migrations of <typeparamref name="TContext"/> as a resource provisioner of kind
    /// <c>EntityFramework</c>, named after the context: the deployment's <c>resources provision</c> (or
    /// <c>AddDeployStepsOnStartup</c> in development) applies them, and a host start fails while one is pending.
    /// </summary>
    /// <typeparam name="TContext">The context to migrate; registered directly or through its <c>IDbContextFactory</c>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    public static IServiceCollection AddDbContextMigrations<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        Argument.IsNotNull(services);

        if (services.Any(static d => d.ServiceType == typeof(DbContextMigrationsMarker<TContext>)))
        {
            return services;
        }

        services.AddSingleton<DbContextMigrationsMarker<TContext>>();

        return services.AddResourceProvisioner(static sp => new DbContextMigrationsProvisioner<TContext>(sp));
    }

    // Makes a second call for the same context a no-op, so a feature's setup may run more than once.
    private sealed class DbContextMigrationsMarker<TContext>
        where TContext : DbContext;
}
