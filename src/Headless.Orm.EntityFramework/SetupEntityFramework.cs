// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.AuditLog;
using Headless.Checks;
using Headless.CommitCoordination.EntityFramework;
using Headless.Core;
using Headless.Domain;
using Headless.EntityFramework.CompiledQueryCache;
using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework;

[PublicAPI]
public static class SetupEntityFramework
{
    extension(IServiceCollection services)
    {
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

        public IHeadlessDbContextBuilder AddHeadlessDbContext<TDbContext>(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            Action<HeadlessDbContextOptions>? configureHeadlessOptions,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessDbContext
        {
            var builder = services.AddHeadlessDbContextServices(configureHeadlessOptions);

            // EF Core does not auto-discover IInterceptor registrations from the application container. A
            // DI-registered IDbContextOptionsConfiguration<TDbContext> attaches them whenever EF Core builds this
            // context's options — covering this AddDbContext registration AND a consumer's own plain
            // AddDbContext<TDbContext>. Deduped by reference, so the consumer's own options-action adds are safe.
            services.AddDiRegisteredInterceptorsConfiguration<TDbContext>();

            services.AddDbContext<TDbContext>(
                (serviceProvider, optionsBuilder) =>
                {
                    optionsAction?.Invoke(serviceProvider, optionsBuilder);
                    optionsBuilder.AddHeadlessExtension();
                },
                contextLifetime,
                optionsLifetime
            );

            // Register IDbContextFactory<TDbContext> alongside AddDbContext so consumers can
            // resolve detached contexts (background work, IInitializer, BackgroundService) without
            // a separate AddDbContextFactory call. Singleton wrapper that creates a fresh scope per
            // call and transfers ownership to the returned context.
            services.TryAddSingleton<IDbContextFactory<TDbContext>, HeadlessDbContextFactory<TDbContext>>();

            return builder;
        }

        /// <summary>
        /// Registers an <see cref="IDbContextOptionsConfiguration{TContext}"/> that attaches every
        /// application-registered <see cref="Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor"/> to
        /// <typeparamref name="TDbContext"/>'s options whenever EF Core builds them — including a consumer's own
        /// plain <c>AddDbContext&lt;TDbContext&gt;</c>. EF Core does not auto-discover DI interceptors; this is the
        /// seam that makes package-registered interceptors (e.g. the commit-coordination interceptor) fire. Safe to
        /// call repeatedly (deduped by reference).
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

        public IHeadlessDbContextBuilder AddHeadlessDbContextServices()
        {
            return services.AddHeadlessDbContextServices(configureOptions: null);
        }

        public IHeadlessDbContextBuilder AddHeadlessDbContextServices(
            Action<HeadlessDbContextOptions>? configureOptions
        )
        {
            var options = _GetOrAddHeadlessDbContextOptions(services);
            configureOptions?.Invoke(options);
            options.RegisterServices(services);

            // The SaveChanges pipeline opens a coordinated EF transaction so the messaging outbox (and any
            // other commit-enlisted work) drains atomically when the save commits. Registering the EF commit
            // coordination source + interceptor here is harmless when nothing enlists (the drain is empty).
            services.AddEntityFrameworkCommitCoordination();

            services.AddOptions<TenantWriteGuardOptions>();
            services.TryAddScoped<HeadlessDbContextServices>();
            services.TryAddScoped<IHeadlessSaveChangesPipeline, HeadlessSaveChangesPipeline>();
            services.TryAddScoped<IHeadlessAuditPersistence, HeadlessAuditPersistence>();
            services.TryAddSingleton<IAmbientDbTransactionAccessor, EfAmbientDbTransactionAccessor>();
            // EF change-capture lives alongside the SaveChanges pipeline so any HeadlessDbContext-based
            // consumer gets it wired regardless of which IAuditLogStore (EF/PG/SqlServer) they pick.
            services.TryAddScoped<IAuditChangeCapture, EfAuditChangeCapture>();
            services.TryAddSingleton<ITenantWriteGuardBypass, TenantWriteGuardBypass>();

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock, Clock>();
            services.AddHeadlessGuidGenerator();
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            // Removes NullCurrentTenant fallback; preserves consumer-supplied ICurrentTenant.
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
            services.ReplaceCompiledQueryCacheKeyGenerator();

            return new HeadlessDbContextBuilder(services);
        }

        public IServiceCollection AddHeadlessTenantWriteGuard(Action<TenantWriteGuardOptions>? configure = null)
        {
            return services._AddHeadlessTenantWriteGuardCore(optionsBuilder =>
            {
                if (configure is not null)
                {
                    optionsBuilder.Configure(configure);
                }
            });
        }

        public IServiceCollection AddHeadlessTenantWriteGuard(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            return services._AddHeadlessTenantWriteGuardCore(optionsBuilder => optionsBuilder.Bind(configuration));
        }

        public IServiceCollection AddHeadlessTenantWriteGuard(
            Action<TenantWriteGuardOptions, IServiceProvider> configure
        )
        {
            Argument.IsNotNull(configure);

            return services._AddHeadlessTenantWriteGuardCore(optionsBuilder => optionsBuilder.Configure(configure));
        }

        private IServiceCollection _AddHeadlessTenantWriteGuardCore(
            Action<OptionsBuilder<TenantWriteGuardOptions>> configure
        )
        {
            services.AddHeadlessDbContextServices();

            // Sentinel — guard PostConfigure registration so repeated AddHeadlessTenantWriteGuard()
            // calls do not enqueue the IsEnabled = true PostConfigure callback multiple times. The
            // optioned configure?.Invoke(...) is still applied on every call so callers may layer
            // overrides through repeated calls if they wish.
            var alreadyRegistered = services.Any(d => d.ServiceType == typeof(HeadlessTenantWriteGuardSentinel));
            configure(services.AddOptions<TenantWriteGuardOptions>());

            if (alreadyRegistered)
            {
                return services;
            }

            services.AddSingleton<HeadlessTenantWriteGuardSentinel>();

            // PostConfigure (not Configure): the seam's IsEnabled = true must run AFTER any consumer
            // Configure<TenantWriteGuardOptions>(...) the host wires up so a later host-side
            // Configure that disables the guard does not override the seam's explicit opt-in.
            services.PostConfigure<TenantWriteGuardOptions>(options =>
            {
                options.IsEnabled = true;
            });

            return services;
        }
    }

    /// <summary>
    /// Registers the in-process domain-event bus (<see cref="ILocalEventBus"/>) so entities implementing
    /// <see cref="IDomainEventEmitter"/> have their domain events published within the save transaction.
    /// </summary>
    public static IHeadlessDbContextBuilder AddDomainEvents(this IHeadlessDbContextBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.Services.AddHeadlessLocalEventBus();

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
