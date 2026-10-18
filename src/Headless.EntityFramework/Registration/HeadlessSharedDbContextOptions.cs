// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>
/// The options of a non-pooled context registered with <see cref="ServiceLifetime.Singleton"/> options: extensions
/// built once from the root provider and shared by every scope, each scope getting a copy that carries it as the
/// application provider.
/// </summary>
/// <remarks>
/// <para>
/// Building options costs about 15 KB per scope. Sharing is the caller's assertion that every input is the same for
/// every scope, as with EF Core's own singleton options: nothing here checks it. A check cannot see what matters most,
/// an options action that reads ambient per-tenant state (a static or <c>AsyncLocal</c> connection string, a closure
/// over <c>IHttpContextAccessor</c>), because two builds at startup read the same ambient state; and building twice to
/// compare would run the caller's action twice for an answer that is still not safe to trust.
/// </para>
/// <para>
/// The build follows EF Core's own: a fresh builder bound to the root provider, then every registered
/// <see cref="IDbContextOptionsConfiguration{TContext}"/> in registration order. It runs at the first resolution, so
/// configurations registered after the context are included.
/// </para>
/// </remarks>
internal sealed class HeadlessSharedDbContextOptions<TContext>(IServiceProvider rootServices)
    where TContext : DbContext
{
    private readonly Lock _lock = new();
    private volatile DbContextOptions<TContext>? _shared;

    public static void Register(
        IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction
    )
    {
        // A singleton configuration, so building from the root provider passes scope validation; the action receives
        // the root provider, as it would with EF Core's singleton options.
        services.ConfigureDbContext<TContext>(
            (serviceProvider, optionsBuilder) => optionsAction?.Invoke(serviceProvider, optionsBuilder),
            ServiceLifetime.Singleton
        );

        services.TryAddSingleton(rootServices => new HeadlessSharedDbContextOptions<TContext>(rootServices));
        services.TryAdd(
            ServiceDescriptor.Scoped(serviceProvider =>
                serviceProvider.GetRequiredService<HeadlessSharedDbContextOptions<TContext>>().Get(serviceProvider)
            )
        );
    }

    public DbContextOptions<TContext> Get(IServiceProvider scopedServices)
    {
        if (_shared is not { } shared)
        {
            // Not Lazy<T>: a build that throws (a scoped service resolved from the root under scope validation) must
            // be retried on the next resolution rather than cached for the host's lifetime.
            lock (_lock)
            {
                shared = _shared ??= _Build(rootServices);
            }
        }

        return _ForScope(shared, scopedServices);
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
