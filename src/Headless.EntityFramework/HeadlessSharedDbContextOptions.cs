// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>
/// The options of a non-pooled context registered without an explicit options lifetime: one instance shared by every
/// scope when nothing scoped can feed them, otherwise a new instance per scope, as EF Core builds scoped options.
/// </summary>
/// <remarks>
/// <para>
/// Building options costs about 15 KB per scope, and most applications configure a context the same way in every
/// scope. Sharing is safe only when every input is the same for every scope: the options action registered with the
/// context takes no provider, every DI-registered <see cref="IInterceptor"/> (which the options pick up) is a
/// singleton, and no options configuration for the context (such as a <c>ConfigureDbContext</c> call) is scoped or
/// registered after the context, since one could resolve a per-request connection string or service. The options
/// action must also not create a connection or interceptor of its own: two builds that hold different instances keep
/// the options per scope.
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
    int configurationCount,
    IServiceProvider rootServices
)
    where TContext : DbContext
{
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

        var configurationCount = services.Count(static descriptor =>
            descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>)
        );

        services.TryAddSingleton(rootServices => new HeadlessSharedDbContextOptions<TContext>(
            services,
            configurationCount,
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
            return shared;
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
                    return decided;
                }
            }
        }

        return _Build(scopedServices);
    }

    private void _Decide()
    {
        if (!_RegistrationsAllowSharing())
        {
            _decision = Decision.PerScope;

            return;
        }

        // An action that creates its connection or an interceptor inside the lambda expects one per scope, and
        // sharing would hand that one instance to concurrent requests. Two builds tell it apart from an instance the
        // action captured, which is the same object in both.
        var candidate = _Build(rootServices);

        if (!_HoldSameInstances(candidate, _Build(rootServices)))
        {
            _decision = Decision.PerScope;

            return;
        }

        _shared = candidate;
        _decision = Decision.Shared;
    }

    private bool _RegistrationsAllowSharing()
    {
        var canShare = true;
        var configurations = 0;

        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService)
            {
                continue;
            }

            if (descriptor.ServiceType == typeof(IInterceptor) && descriptor.Lifetime != ServiceLifetime.Singleton)
            {
                canShare = false;
            }
            else if (descriptor.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>))
            {
                configurations++;
                canShare &= descriptor.Lifetime == ServiceLifetime.Singleton;
            }
        }

        return canShare && configurations == configurationCount;
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
