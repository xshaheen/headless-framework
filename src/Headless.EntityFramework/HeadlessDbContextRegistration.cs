// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>
/// The two ways a Headless context is registered, shared by <see cref="HeadlessDbContext"/> and the Identity context
/// so both bases get identical lifetimes, bindings, and factories.
/// </summary>
/// <remarks>
/// Each path registers its own scoped context descriptor before calling EF Core, whose registration uses
/// <c>TryAdd</c> for the context and so keeps ours: the context the scope resolves is bound to that scope. A context
/// registered by the application first, through plain EF Core, is not bound explicitly: with per-scope options it
/// borrows the scope that built them, and with singleton options it opens a private scope on first use.
/// </remarks>
internal static class HeadlessDbContextRegistration
{
    // Keys the context's own implementation-type registration, so DI selects and caches its constructor exactly as a
    // plain EF Core registration would; the unkeyed descriptor resolves it and binds the scope.
    private static readonly object _ImplementationKey = new();

    /// <summary>EF Core's default pool size.</summary>
    public const int DefaultPoolSize = 1024;

    /// <summary>
    /// One context instance per scope (or per resolution, for <see cref="ServiceLifetime.Transient"/>), built with
    /// options of <paramref name="optionsLifetime"/>. A derived constructor may take scoped services.
    /// </summary>
    public static void AddPerScope<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
                | DynamicallyAccessedMemberTypes.NonPublicConstructors
                | DynamicallyAccessedMemberTypes.PublicProperties
        )]
            TDbContext
    >(
        IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
        ServiceLifetime contextLifetime,
        ServiceLifetime optionsLifetime
    )
        where TDbContext : DbContext, IHeadlessDbContext
    {
        // A singleton context would bind the root provider and serve every request with one change tracker.
        Argument.IsNotEqualTo(contextLifetime, ServiceLifetime.Singleton);

        services.AddDiRegisteredInterceptorsConfiguration<TDbContext>();

        services.TryAdd(
            new ServiceDescriptor(typeof(TDbContext), _ImplementationKey, typeof(TDbContext), contextLifetime)
        );
        services.TryAdd(
            new ServiceDescriptor(
                typeof(TDbContext),
                sp => _Bind(sp.GetRequiredKeyedService<TDbContext>(_ImplementationKey), sp),
                contextLifetime
            )
        );

        services.AddDbContext<TDbContext>(
            (serviceProvider, optionsBuilder) =>
            {
                optionsAction?.Invoke(serviceProvider, optionsBuilder);
                optionsBuilder.AddHeadlessExtension();
            },
            contextLifetime,
            optionsLifetime
        );

        // Detached contexts (background work, initializers, singleton stores) come from a factory that opens a scope
        // per context, so a derived constructor's scoped services still resolve.
        services.TryAddSingleton<IDbContextFactory<TDbContext>, HeadlessDbContextFactory<TDbContext>>();
    }

    /// <summary>
    /// Pooled context instances leased per scope and from <see cref="IDbContextFactory{TContext}"/>, built once from
    /// singleton options. A derived constructor may take only its options and singleton services.
    /// </summary>
    public static void AddPooled<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors
                | DynamicallyAccessedMemberTypes.NonPublicConstructors
                | DynamicallyAccessedMemberTypes.PublicProperties
        )]
            TDbContext
    >(IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction, int poolSize)
        where TDbContext : DbContext, IHeadlessDbContext
    {
        Argument.IsPositive(poolSize);

        services.AddDiRegisteredInterceptorsConfiguration<TDbContext>();

        // The scope's lease returns the instance once, at scope end; a consumer disposing the context earlier is a
        // no-op. DI disposes the context (no-op while leased) before the lease, which it resolved first.
        services.TryAddScoped<HeadlessScopedDbContextLease<TDbContext>>();
        services.TryAddScoped(sp => sp.GetRequiredService<HeadlessScopedDbContextLease<TDbContext>>().Context);

        // EF's PooledDbContextFactory leases unbound contexts, which open a private scope only when a save or the
        // write guard first needs a scoped collaborator, so a read-only factory context creates no scope at all.
        services.AddPooledDbContextFactory<TDbContext>(
            (serviceProvider, optionsBuilder) =>
            {
                optionsAction?.Invoke(serviceProvider, optionsBuilder);
                optionsBuilder.AddHeadlessExtension();
            },
            poolSize
        );
    }

    private static TDbContext _Bind<TDbContext>(TDbContext context, IServiceProvider services)
        where TDbContext : DbContext, IHeadlessDbContext
    {
        if (context is not IHeadlessDbContextRuntimeOwner owner)
        {
            throw new InvalidOperationException(
                $"`{typeof(TDbContext).FullName}` must derive from HeadlessDbContext or HeadlessIdentityDbContext."
            );
        }

        // A transient context resolved from the root would otherwise bind the root, and scoped collaborators resolved
        // from it would live for the host's lifetime; left unbound, it opens a private scope instead.
        if (!HeadlessDbContextRuntime.IsRootProvider(services))
        {
            owner.Runtime.Bind(services);
        }

        return context;
    }
}
