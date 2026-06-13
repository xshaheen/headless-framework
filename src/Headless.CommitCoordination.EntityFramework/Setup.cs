// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Registers EF Core commit coordination services.
/// </summary>
[PublicAPI]
public static class SetupEntityFrameworkCommitCoordination
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds EF Core commit coordination services.
        /// </summary>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddEntityFrameworkCommitCoordination()
        {
            services.AddCommitCoordination();
            services.TryAddSingleton<EntityFrameworkCommitSignalSource>();
            services.TryAddSingleton<ICommitSignalSource>(sp =>
                sp.GetRequiredService<EntityFrameworkCommitSignalSource>()
            );

            // IMPORTANT: EF Core does NOT auto-discover IInterceptor registrations from the application
            // service provider — the interceptor must be added to the context options explicitly or the
            // commit/rollback edges are never observed (and coordinated work silently drains as rollback).
            // The Headless ORM path (AddHeadlessDbContext / AddHeadlessIdentityDbContext) applies DI-registered
            // interceptors automatically; plain AddDbContext consumers must opt in themselves:
            //   services.AddDbContext<MyDbContext>((sp, options) =>
            //       options.UseNpgsql(...).AddInterceptors(sp.GetServices<IInterceptor>()));
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IInterceptor, CommitCoordinationTransactionInterceptor>()
            );

            return services;
        }

        /// <summary>
        /// Registers an <see cref="IDbContextOptionsConfiguration{TContext}"/> for the given
        /// <paramref name="dbContextType"/> that auto-attaches the commit-coordination transaction
        /// interceptor while EF Core builds that context's options — including a plain
        /// <c>AddDbContext&lt;TContext&gt;</c> with no consumer <c>AddInterceptors</c> call.
        /// </summary>
        /// <remarks>
        /// Pair this with <c>AddEntityFrameworkCommitCoordination()</c>, which registers the interceptor
        /// itself. EF Core allows multiple option configurations per context; the configuration dedupes the
        /// interceptor by reference, so repeated registration is safe. Only the commit-coordination interceptor
        /// is attached, never arbitrary DI-registered interceptors.
        /// </remarks>
        /// <param name="dbContextType">The runtime <see cref="DbContext"/> type to configure.</param>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddCommitCoordinationDbContextConfiguration(Type dbContextType)
        {
            Argument.IsNotNull(dbContextType);

            // MakeGenericType enforces the `where TContext : DbContext` constraint at runtime.
            var serviceType = typeof(IDbContextOptionsConfiguration<>).MakeGenericType(dbContextType);
            var implementationType = typeof(CommitCoordinationOptionsConfiguration<>).MakeGenericType(dbContextType);

            services.TryAddEnumerable(ServiceDescriptor.Singleton(serviceType, implementationType));

            return services;
        }
    }
}
