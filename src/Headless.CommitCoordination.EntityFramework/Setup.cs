// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    }
}
