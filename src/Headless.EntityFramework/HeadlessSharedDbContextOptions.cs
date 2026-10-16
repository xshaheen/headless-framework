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
/// singleton, and nothing else (such as a <c>ConfigureDbContext</c> call) adds an options configuration for the
/// context, since one could resolve a per-request connection string or service.
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
    private const int _Undecided = 0;
    private const int _Shared = 1;
    private const int _PerScope = 2;

    private readonly Lock _lock = new();
    private DbContextOptions<TContext>? _shared;
    private volatile int _decision;

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

        if (!_CanShare())
        {
            return _Build(scopedServices);
        }

        lock (_lock)
        {
            return _shared ??= _Build(rootServices);
        }
    }

    private bool _CanShare()
    {
        if (_decision != _Undecided)
        {
            return _decision == _Shared;
        }

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

        // The registrations are fixed once the container is built, so every racing caller reaches the same answer.
        _decision = canShare && configurations == configurationCount ? _Shared : _PerScope;

        return _decision == _Shared;
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
}
