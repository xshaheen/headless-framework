// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Headless.Checks;
using Headless.Domain;
using Headless.EntityFramework.CompiledQueryCache;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>
/// Extension members for registering Headless EF Core contexts and infrastructure services
/// on <see cref="IServiceCollection"/>.
/// </summary>
[PublicAPI]
public static class SetupEntityFramework
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <typeparamref name="TDbContext"/>, one instance per scope, together with the full Headless EF Core
        /// service set (save pipeline, audit, multi-tenancy, domain-event bus bridge, compiled-query cache). Use
        /// <c>AddHeadlessDbContextPool</c> to pool the context instead.
        /// </summary>
        /// <remarks>
        /// The context resolved from a scope is bound to that scope. <see cref="IDbContextFactory{TContext}"/> is
        /// registered too: it creates a scope per context and disposes it with the context, so a derived constructor
        /// may take scoped services.
        /// </remarks>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">Optional EF Core options callback.</param>
        /// <param name="contextLifetime">DI lifetime for <typeparamref name="TDbContext"/>: <see cref="ServiceLifetime.Scoped"/> (the default) or <see cref="ServiceLifetime.Transient"/>; <see cref="ServiceLifetime.Singleton"/> throws <see cref="ArgumentException"/>.</param>
        /// <param name="optionsLifetime">DI lifetime for <see cref="DbContextOptions{TContext}"/>. Defaults to <see cref="ServiceLifetime.Scoped"/>.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<TDbContext>(
            Action<DbContextOptionsBuilder>? optionsAction,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessDbContext
        {
            return services.AddHeadlessDbContext<TDbContext>(
                optionsAction,
                configureHeadlessOptions: null,
                contextLifetime,
                optionsLifetime
            );
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> together with the full Headless EF Core service set,
        /// with an additional callback to configure the Headless save pipeline options.
        /// </summary>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">Optional EF Core options callback.</param>
        /// <param name="configureHeadlessOptions">
        /// Optional callback to customize <see cref="HeadlessDbContextOptions"/> (for example to add or
        /// replace save-entry processors).
        /// </param>
        /// <param name="contextLifetime">DI lifetime for <typeparamref name="TDbContext"/>: <see cref="ServiceLifetime.Scoped"/> (the default) or <see cref="ServiceLifetime.Transient"/>; <see cref="ServiceLifetime.Singleton"/> throws <see cref="ArgumentException"/>.</param>
        /// <param name="optionsLifetime">DI lifetime for <see cref="DbContextOptions{TContext}"/>.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<TDbContext>(
            Action<DbContextOptionsBuilder>? optionsAction,
            Action<HeadlessDbContextOptions>? configureHeadlessOptions,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessDbContext
        {
            return services.AddHeadlessDbContext<TDbContext>(
                (_, ob) => optionsAction?.Invoke(ob),
                configureHeadlessOptions,
                contextLifetime,
                optionsLifetime
            );
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> together with the full Headless EF Core service set.
        /// The options callback receives the ambient <see cref="IServiceProvider"/> for provider-specific wiring.
        /// </summary>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">Optional EF Core options callback that receives the scoped service provider.</param>
        /// <param name="contextLifetime">DI lifetime for <typeparamref name="TDbContext"/>: <see cref="ServiceLifetime.Scoped"/> (the default) or <see cref="ServiceLifetime.Transient"/>; <see cref="ServiceLifetime.Singleton"/> throws <see cref="ArgumentException"/>.</param>
        /// <param name="optionsLifetime">DI lifetime for <see cref="DbContextOptions{TContext}"/>.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<TDbContext>(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessDbContext
        {
            return services.AddHeadlessDbContext<TDbContext>(
                optionsAction,
                configureHeadlessOptions: null,
                contextLifetime,
                optionsLifetime
            );
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> together with the full Headless EF Core service set.
        /// The options callback receives the ambient <see cref="IServiceProvider"/> and an additional
        /// callback allows customizing the Headless save pipeline options.
        /// </summary>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">Optional EF Core options callback that receives the scoped service provider.</param>
        /// <param name="configureHeadlessOptions">
        /// Optional callback to customize <see cref="HeadlessDbContextOptions"/>.
        /// </param>
        /// <param name="contextLifetime">DI lifetime for <typeparamref name="TDbContext"/>: <see cref="ServiceLifetime.Scoped"/> (the default) or <see cref="ServiceLifetime.Transient"/>; <see cref="ServiceLifetime.Singleton"/> throws <see cref="ArgumentException"/>.</param>
        /// <param name="optionsLifetime">DI lifetime for <see cref="DbContextOptions{TContext}"/>.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContext<TDbContext>(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            Action<HeadlessDbContextOptions>? configureHeadlessOptions,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessDbContext
        {
            var builder = services.AddHeadlessDbContextServices(configureHeadlessOptions);
            HeadlessDbContextRegistration.AddPerScope<TDbContext>(
                services,
                optionsAction,
                contextLifetime,
                optionsLifetime
            );

            return builder;
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> as a pooled context together with the full Headless EF Core
        /// service set.
        /// </summary>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">Optional EF Core options callback, run once to build singleton options.</param>
        /// <param name="poolSize">The maximum number of instances the pool retains. Defaults to 1024.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        /// <remarks><inheritdoc cref="AddHeadlessDbContextPool{TDbContext}(IServiceCollection, Action{IServiceProvider, DbContextOptionsBuilder}?, Action{HeadlessDbContextOptions}?, int)" path="/remarks/node()"/></remarks>
        public IHeadlessDbContextBuilder AddHeadlessDbContextPool<TDbContext>(
            Action<DbContextOptionsBuilder>? optionsAction,
            int poolSize = HeadlessDbContextRegistration.DefaultPoolSize
        )
            where TDbContext : HeadlessDbContext
        {
            return services.AddHeadlessDbContextPool<TDbContext>(
                (_, ob) => optionsAction?.Invoke(ob),
                configureHeadlessOptions: null,
                poolSize
            );
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> as a pooled context together with the full Headless EF Core
        /// service set, with a callback to configure the Headless save pipeline options.
        /// </summary>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">Optional EF Core options callback, run once to build singleton options.</param>
        /// <param name="configureHeadlessOptions">Optional callback to customize <see cref="HeadlessDbContextOptions"/>.</param>
        /// <param name="poolSize">The maximum number of instances the pool retains. Defaults to 1024.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        /// <remarks><inheritdoc cref="AddHeadlessDbContextPool{TDbContext}(IServiceCollection, Action{IServiceProvider, DbContextOptionsBuilder}?, Action{HeadlessDbContextOptions}?, int)" path="/remarks/node()"/></remarks>
        public IHeadlessDbContextBuilder AddHeadlessDbContextPool<TDbContext>(
            Action<DbContextOptionsBuilder>? optionsAction,
            Action<HeadlessDbContextOptions>? configureHeadlessOptions,
            int poolSize = HeadlessDbContextRegistration.DefaultPoolSize
        )
            where TDbContext : HeadlessDbContext
        {
            return services.AddHeadlessDbContextPool<TDbContext>(
                (_, ob) => optionsAction?.Invoke(ob),
                configureHeadlessOptions,
                poolSize
            );
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> as a pooled context together with the full Headless EF Core
        /// service set. The options callback receives the root <see cref="IServiceProvider"/>.
        /// </summary>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">
        /// Optional EF Core options callback, run once with the root service provider to build singleton options.
        /// </param>
        /// <param name="poolSize">The maximum number of instances the pool retains. Defaults to 1024.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        /// <remarks><inheritdoc cref="AddHeadlessDbContextPool{TDbContext}(IServiceCollection, Action{IServiceProvider, DbContextOptionsBuilder}?, Action{HeadlessDbContextOptions}?, int)" path="/remarks/node()"/></remarks>
        public IHeadlessDbContextBuilder AddHeadlessDbContextPool<TDbContext>(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            int poolSize = HeadlessDbContextRegistration.DefaultPoolSize
        )
            where TDbContext : HeadlessDbContext
        {
            return services.AddHeadlessDbContextPool<TDbContext>(
                optionsAction,
                configureHeadlessOptions: null,
                poolSize
            );
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> as a pooled context together with the full Headless EF Core
        /// service set. The options callback receives the root <see cref="IServiceProvider"/>, and an additional
        /// callback customizes the Headless save pipeline options.
        /// </summary>
        /// <typeparam name="TDbContext">The <see cref="HeadlessDbContext"/> subclass to register.</typeparam>
        /// <param name="optionsAction">
        /// Optional EF Core options callback, run once with the root service provider to build singleton options.
        /// </param>
        /// <param name="configureHeadlessOptions">Optional callback to customize <see cref="HeadlessDbContextOptions"/>.</param>
        /// <param name="poolSize">The maximum number of instances the pool retains. Defaults to 1024.</param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        /// <remarks>
        /// <para>
        /// Pooling reuses context instances across scopes, which saves the per-request construction and service
        /// resolution of a context. The context resolved from a scope is leased from the pool and bound to that
        /// scope; disposing the scope returns it. <see cref="IDbContextFactory{TContext}"/> leases from the same pool:
        /// a factory context opens a private scope only when it first needs a scoped collaborator, such as the save
        /// pipeline, and disposes it with the context.
        /// </para>
        /// <para>
        /// The options are built once, so the callback must not depend on scoped services. The context must declare
        /// exactly one public constructor, taking its <c>DbContextOptions</c> and optionally singleton services, and
        /// must not keep per-request state in its own fields.
        /// </para>
        /// </remarks>
        public IHeadlessDbContextBuilder AddHeadlessDbContextPool<TDbContext>(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            Action<HeadlessDbContextOptions>? configureHeadlessOptions,
            int poolSize = HeadlessDbContextRegistration.DefaultPoolSize
        )
            where TDbContext : HeadlessDbContext
        {
            var builder = services.AddHeadlessDbContextServices(configureHeadlessOptions);
            HeadlessDbContextRegistration.AddPooled<TDbContext>(services, optionsAction, poolSize);

            return builder;
        }

        /// <summary>
        /// Registers an <see cref="IDbContextOptionsConfiguration{TContext}"/> that attaches every
        /// application-registered <see cref="Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor"/> to
        /// <typeparamref name="TDbContext"/>'s options whenever EF Core builds them — including a consumer's own
        /// plain <c>AddDbContext&lt;TDbContext&gt;</c>. EF Core does not auto-discover DI interceptors; this is the
        /// seam that makes package-registered interceptors fire. Safe to call repeatedly (deduped by reference).
        /// </summary>
        /// <returns>The service collection.</returns>
        public IServiceCollection AddDiRegisteredInterceptorsConfiguration<TDbContext>()
            where TDbContext : DbContext
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IDbContextOptionsConfiguration<TDbContext>,
                    DiRegisteredInterceptorsOptionsConfiguration<TDbContext>
                >()
            );

            return services;
        }

        /// <summary>
        /// Registers the Headless EF Core infrastructure services (save pipeline, audit, multi-tenancy,
        /// compiled-query cache, clock, GUID generator) without registering a specific
        /// <see cref="HeadlessDbContext"/> subclass. Use when you need the core services but register the
        /// DbContext separately.
        /// </summary>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContextServices()
        {
            return services.AddHeadlessDbContextServices(configureOptions: null);
        }

        /// <summary>
        /// Registers the Headless EF Core infrastructure services with a callback to customize the
        /// save-pipeline options.
        /// </summary>
        /// <param name="configureOptions">
        /// Optional callback to configure <see cref="HeadlessDbContextOptions"/> before the services are
        /// registered (for example to add custom save-entry processors).
        /// </param>
        /// <returns>A builder for chaining additional Headless EF Core registrations.</returns>
        public IHeadlessDbContextBuilder AddHeadlessDbContextServices(
            Action<HeadlessDbContextOptions>? configureOptions
        )
        {
            var options = _GetOrAddHeadlessDbContextOptions(services);
            configureOptions?.Invoke(options);
            options.RegisterServices(services);

            services.AddOptions<TenantGuardOptions>();
            services.TryAddSingleton<HeadlessRootServiceProvider>();
            services.TryAddScoped<IHeadlessSaveChangesPipeline, HeadlessSaveChangesPipeline>();
            // The save pipeline enlists its transaction in the scoped unit of work and resolves the unit bound
            // to a context through the EF provider's binding — both live in Headless.UnitOfWork.EntityFramework.
            services.AddEntityFrameworkUnitOfWork();
            services.TryAddScoped<IHeadlessAuditPersistence, HeadlessAuditPersistence>();
            services.TryAddSingleton<IAmbientDbTransactionAccessor, EfAmbientDbTransactionAccessor>();
            // EF change-capture lives alongside the SaveChanges pipeline so any HeadlessDbContext-based
            // consumer gets it wired regardless of which IAuditLogStore (EF/PG/SqlServer) they pick.
            services.TryAddScoped<IAuditChangeCapture, EfAuditChangeCapture>();
            services.TryAddSingleton<ITenantWriteGuardBypass, TenantWriteGuardBypass>();

            services.TryAddSingleton(TimeProvider.System);
            services.AddHeadlessGuidGenerator();
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            // Removes NullCurrentTenant fallback; preserves consumer-supplied ICurrentTenant.
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
            // A context created outside a scope (a factory or pooled lease, a Jobs coordinated write, a storage store)
            // reads the tenant from the root provider, as the framework's other singletons do. A scoped or transient
            // ICurrentTenant would be captured there for the host's lifetime, so the host refuses to start instead.
            services.RequireSingletonService<ICurrentTenant>(
                requiredBy: "Headless EF Core tenant query filters and write guard",
                remedy: "Register ICurrentTenant as a singleton. The framework's CurrentTenant reads the ambient tenant from an AsyncLocal; scope a tenant with ICurrentTenant.Change(tenantId), not with a scoped registration."
            );
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
            services.ReplaceCompiledQueryCacheKeyGenerator();

            return new HeadlessDbContextBuilder(services);
        }

        /// <summary>
        /// Enables the EF Core tenant write guard, which rejects cross-tenant mutations and mutations that
        /// lack an ambient tenant context. Also implicitly calls
        /// <c>AddHeadlessDbContextServices()</c> when needed.
        /// </summary>
        /// <returns>The service collection.</returns>
        internal IServiceCollection AddHeadlessTenantWriteGuard()
        {
            services.AddHeadlessDbContextServices();

            // Register enablement once so repeated builder calls remain idempotent.
            if (services.Any(d => d.ServiceType == typeof(HeadlessTenantWriteGuardSentinel)))
            {
                return services;
            }

            services.AddSingleton<HeadlessTenantWriteGuardSentinel>();

            // PostConfigure (not Configure): the seam's GuardWrites = true must run AFTER any consumer
            // Configure<TenantGuardOptions>(...) the host wires up so a later host-side
            // Configure that disables the guard does not override the seam's explicit opt-in.
            services.PostConfigure<TenantGuardOptions>(options => options.GuardWrites = true);

            return services;
        }

        /// <summary>
        /// Enables the EF Core tenant read guard, which makes queries over tenant-owned entities with a
        /// required tenant column throw when no ambient tenant is set. Also implicitly calls
        /// <c>AddHeadlessDbContextServices()</c> when needed.
        /// </summary>
        /// <returns>The service collection.</returns>
        internal IServiceCollection AddHeadlessTenantReadGuard()
        {
            services.AddHeadlessDbContextServices();

            // Register enablement once so repeated builder calls remain idempotent.
            if (services.Any(d => d.ServiceType == typeof(HeadlessTenantReadGuardSentinel)))
            {
                return services;
            }

            services.AddSingleton<HeadlessTenantReadGuardSentinel>();

            // PostConfigure for the same reason as the write guard: the seam's opt-in must win over any
            // host-side Configure<TenantGuardOptions>(...).
            services.PostConfigure<TenantGuardOptions>(options => options.GuardReads = true);

            return services;
        }
    }

    /// <summary>
    /// Registers the in-process domain-event bus (<see cref="IDomainEventDispatcher"/>) so entities implementing
    /// <see cref="IDomainEventEmitter"/> have their domain events published within the save transaction.
    /// </summary>
    public static IHeadlessDbContextBuilder AddDomainEvents(this IHeadlessDbContextBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.Services.AddHeadlessDomainEventDispatcher();

        return builder;
    }

    private static HeadlessDbContextOptions _GetOrAddHeadlessDbContextOptions(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(x => x.ServiceType == typeof(HeadlessDbContextOptions));

        if (descriptor?.ImplementationInstance is HeadlessDbContextOptions options)
        {
            return options;
        }

        options = new HeadlessDbContextOptions();
        services.Replace(ServiceDescriptor.Singleton(options));

        return options;
    }
}

/// <summary>Sentinel marker for one-shot tenant-write-guard PostConfigure registration.</summary>
internal sealed class HeadlessTenantWriteGuardSentinel;

/// <summary>Sentinel marker for one-shot tenant-read-guard PostConfigure registration.</summary>
internal sealed class HeadlessTenantReadGuardSentinel;
