// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        /// Adds EF Core commit coordination services: the core infrastructure, the
        /// <see cref="EntityFrameworkCommitSignalSource" />, and the
        /// <see cref="CommitCoordinationTransactionInterceptor" /> (registered as an <c>IInterceptor</c>
        /// singleton).
        /// </summary>
        /// <remarks>
        /// EF Core does <b>not</b> auto-discover <c>IInterceptor</c> registrations from the application service
        /// provider — the interceptor must be added to the context options explicitly. The Headless ORM path
        /// (<c>AddHeadlessDbContext</c> / <c>AddHeadlessIdentityDbContext</c>) applies DI-registered interceptors
        /// automatically. Plain <c>AddDbContext</c> consumers must opt in:
        /// <code>
        /// services.AddDbContext&lt;MyDbContext&gt;((sp, options) =>
        ///     options.UseNpgsql(...).AddInterceptors(sp.GetServices&lt;IInterceptor&gt;()));
        /// </code>
        /// Idempotent: repeated calls register each service at most once.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection" /> for chaining.</returns>
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
        internal IServiceCollection AddCommitCoordinationDbContextConfiguration(Type dbContextType)
        {
            Argument.IsNotNull(dbContextType);

            // MakeGenericType enforces the `where TContext : DbContext` constraint at runtime.
            var serviceType = typeof(IDbContextOptionsConfiguration<>).MakeGenericType(dbContextType);
            var implementationType = typeof(CommitCoordinationOptionsConfiguration<>).MakeGenericType(dbContextType);

            services.TryAddEnumerable(ServiceDescriptor.Singleton(serviceType, implementationType));

            return services;
        }

        /// <summary>
        /// Registers the startup self-probe (<see cref="CommitInterceptorStartupGate{TContext}"/>) for the given
        /// <paramref name="dbContextType"/>. It verifies — before any hosted service runs — that the commit
        /// interceptor actually fires for that context, and surfaces a mis-wire loudly per
        /// <see cref="CommitInterceptorProbeOptions"/> (Warn by default, Strict opt-in). The probe is side-effect
        /// free (commits an empty transaction; no consumer data is mutated).
        /// </summary>
        /// <param name="dbContextType">The runtime <see cref="DbContext"/> type to probe.</param>
        /// <returns>The service collection.</returns>
        internal IServiceCollection AddCommitInterceptorStartupGate(Type dbContextType)
        {
            Argument.IsNotNull(dbContextType);

            services.AddOptions<CommitInterceptorProbeOptions>();

            // MakeGenericType enforces the `where TContext : DbContext` constraint at runtime. Registered as
            // IHostedService; the host detects the IHostedLifecycleService hooks and runs StartingAsync first.
            var gateType = typeof(CommitInterceptorStartupGate<>).MakeGenericType(dbContextType);
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IHostedService), gateType));

            return services;
        }

        /// <summary>
        /// Wires the full transactional-outbox stack for <paramref name="dbContextType"/> in one call: the EF commit
        /// signal source (<see cref="AddEntityFrameworkCommitCoordination"/>), the interceptor-attach options
        /// configuration (<see cref="AddCommitCoordinationDbContextConfiguration"/>), and the startup self-probe gate
        /// (<see cref="AddCommitInterceptorStartupGate"/>). Callers own the enable/opt-out policy (EF-context vs
        /// raw-ADO path, consumer opt-out) and call this only once that policy resolves to "wire it".
        /// </summary>
        /// <param name="dbContextType">The runtime <see cref="DbContext"/> type to wire.</param>
        /// <returns>The service collection.</returns>
        internal IServiceCollection AddCommitCoordinationWithStartupGate(Type dbContextType)
        {
            Argument.IsNotNull(dbContextType);

            services.AddEntityFrameworkCommitCoordination();
            services.AddCommitCoordinationDbContextConfiguration(dbContextType);
            services.AddCommitInterceptorStartupGate(dbContextType);

            return services;
        }
    }
}
