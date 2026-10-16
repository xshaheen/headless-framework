// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>
/// The options of a non-pooled context registered without an explicit options lifetime: extensions built once and
/// shared by every scope when nothing scoped can feed them, each scope getting a copy that carries it as the application
/// provider; otherwise a new build per scope, as EF Core builds scoped options.
/// </summary>
/// <remarks>
/// <para>
/// Building options costs about 15 KB per scope, and most applications configure a context the same way in every
/// scope. Sharing is safe only when every input is the same for every scope: the options action registered with the
/// context takes no provider, every DI-registered <see cref="IInterceptor"/> (which the options pick up) is a
/// singleton, and the only options configurations for the context are the two the framework registers. Any other
/// configuration (such as a <c>ConfigureDbContext</c> call), whatever its lifetime, keeps the options per scope: it
/// receives the provider that builds the options and may resolve a per-request connection string or service from it,
/// which a shared build would resolve once from the root. The options action must also not create a connection or
/// interceptor of its own: two builds that hold different instances keep the options per scope.
/// </para>
/// <para>
/// Only registrations in the <see cref="IServiceCollection"/> are inspected. A scoped interceptor registered directly
/// in a third-party container is not seen, so such an application passes an explicit options lifetime.
/// </para>
/// <para>
/// The decision is made at the first resolution from the registrations as they stand once the container is built,
/// so an interceptor or configuration registered after the context still keeps the options scoped. The build follows
/// EF Core's own: a fresh builder bound to the application provider, then every registered
/// <see cref="IDbContextOptionsConfiguration{TContext}"/> in registration order.
/// </para>
/// </remarks>
internal sealed class HeadlessSharedDbContextOptions<TContext>(
    IServiceCollection services,
    ServiceDescriptor actionConfiguration,
    IServiceProvider rootServices
)
    where TContext : DbContext
{
    // Read once, by the decision; released after it so the descriptor list is not kept for the host's lifetime.
    private IServiceCollection? _services = services;
    private readonly Lock _lock = new();
    private volatile DbContextOptions<TContext>? _shared;
    private volatile Decision _decision;

    public static void Register(
        IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction
    )
    {
        // A singleton configuration, so building from the root provider passes scope validation. The action takes no
        // provider when it reaches this path; the parameter only matches EF Core's configuration shape.
        services.ConfigureDbContext<TContext>(
            (serviceProvider, optionsBuilder) => optionsAction?.Invoke(serviceProvider, optionsBuilder),
            ServiceLifetime.Singleton
        );

        // ConfigureDbContext appends its descriptor, so the last one is the action's.
        var actionConfiguration = services.Last(static descriptor =>
            descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>)
        );

        services.TryAddSingleton(rootServices => new HeadlessSharedDbContextOptions<TContext>(
            services,
            actionConfiguration,
            rootServices
        ));
        services.TryAdd(
            ServiceDescriptor.Scoped(serviceProvider =>
                serviceProvider.GetRequiredService<HeadlessSharedDbContextOptions<TContext>>().Get(serviceProvider)
            )
        );
    }

    public DbContextOptions<TContext> Get(IServiceProvider scopedServices)
    {
        if (_shared is { } shared)
        {
            return _ForScope(shared, scopedServices);
        }

        if (_decision != Decision.PerScope)
        {
            lock (_lock)
            {
                if (_decision == Decision.Undecided)
                {
                    _Decide();
                }

                if (_shared is { } decided)
                {
                    return _ForScope(decided, scopedServices);
                }
            }
        }

        return _Build(scopedServices);
    }

    // The extensions are built once and shared; only the application provider is the scope's. A context created by
    // hand over these options (new AppDb(options)) therefore adopts the request scope, and its services resolve from
    // that scope, exactly as with options built per scope. EF Core keys its internal service provider on the
    // extensions without the application provider, so every scope still shares one internal provider.
    private static DbContextOptions<TContext> _ForScope(DbContextOptions<TContext> shared, IServiceProvider scope)
    {
        var core = shared.FindExtension<CoreOptionsExtension>()!;

        return (DbContextOptions<TContext>)shared.WithExtension(core.WithApplicationServiceProvider(scope));
    }

    private void _Decide()
    {
        if (!_RegistrationsAllowSharing())
        {
            _decision = Decision.PerScope;
            _services = null;

            return;
        }

        // An action that creates its connection or an interceptor inside the lambda expects one per scope, and
        // sharing would hand that one instance to concurrent requests. Two builds tell it apart from an instance the
        // action captured, which is the same object in both.
        var candidate = _Build(rootServices);

        var same = _HoldSameInstances(candidate, _Build(rootServices));
        _services = null;

        if (!same)
        {
            _decision = Decision.PerScope;

            return;
        }

        _shared = candidate;
        _decision = Decision.Shared;
    }

    private bool _RegistrationsAllowSharing()
    {
        foreach (var descriptor in _services!)
        {
            if (descriptor.IsKeyedService)
            {
                continue;
            }

            if (descriptor.ServiceType == typeof(IInterceptor) && descriptor.Lifetime != ServiceLifetime.Singleton)
            {
                return false;
            }

            // A registration's lifetime says nothing about what its Configure resolves from the provider it is handed,
            // so only the framework's own two configurations are known to read nothing per scope.
            if (
                descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>)
                && !ReferenceEquals(descriptor, actionConfiguration)
                && descriptor.ImplementationType != typeof(DiRegisteredInterceptorsOptionsConfiguration<TContext>)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool _HoldSameInstances(DbContextOptions<TContext> first, DbContextOptions<TContext> second)
    {
        foreach (var extension in first.Extensions)
        {
            if (extension is RelationalOptionsExtension relational)
            {
                var other = second.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();

                if (!ReferenceEquals(relational.Connection, other?.Connection))
                {
                    return false;
                }
            }
        }

        var firstInterceptors = first.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];
        var secondInterceptors = second.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        return firstInterceptors.SequenceEqual(secondInterceptors, ReferenceEqualityComparer.Instance);
    }

    private static DbContextOptions<TContext> _Build(IServiceProvider applicationServices)
    {
        var builder = new DbContextOptionsBuilder<TContext>(
            new DbContextOptions<TContext>(new Dictionary<Type, IDbContextOptionsExtension>())
        );
        builder.UseApplicationServiceProvider(applicationServices);

        foreach (var configuration in applicationServices.GetServices<IDbContextOptionsConfiguration<TContext>>())
        {
            configuration.Configure(applicationServices, builder);
        }

        return builder.Options;
    }

    private enum Decision
    {
        Undecided = 0,
        Shared = 1,
        PerScope = 2,
    }
}
