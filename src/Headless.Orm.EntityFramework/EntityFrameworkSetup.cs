// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.EntityFramework;
using Headless.EntityFramework.GlobalFilters;
using Headless.EntityFramework.Messaging;
using Headless.EntityFramework.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

[PublicAPI]
public static class EntityFrameworkSetup
{
    /// <summary>Configures Entity Framework tenant posture through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The Entity Framework tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    public static HeadlessTenancyBuilder EntityFramework(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessEntityFrameworkTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessEntityFrameworkTenancyBuilder(builder));

        return builder;
    }

    extension(IServiceCollection services)
    {
        public IServiceCollection AddHeadlessDbContext<TDbContext>(
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

        public IServiceCollection AddHeadlessDbContext<TDbContext>(
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

        public IServiceCollection AddHeadlessDbContext<TDbContext>(
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

        public IServiceCollection AddHeadlessDbContext<TDbContext>(
            Action<IServiceProvider, DbContextOptionsBuilder>? optionsAction,
            Action<HeadlessDbContextOptions>? configureHeadlessOptions,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
            ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
        )
            where TDbContext : HeadlessDbContext
        {
            services.AddHeadlessDbContextServices(configureHeadlessOptions);

            services.AddDbContext<TDbContext>(
                (serviceProvider, optionsBuilder) =>
                {
                    optionsAction?.Invoke(serviceProvider, optionsBuilder);
                    optionsBuilder.AddHeadlessExtension();
                },
                contextLifetime,
                optionsLifetime
            );

            return services;
        }

        public IServiceCollection AddHeadlessDbContextServices()
        {
            return services.AddHeadlessDbContextServices(configureOptions: null);
        }

        public IServiceCollection AddHeadlessDbContextServices(Action<HeadlessDbContextOptions>? configureOptions)
        {
            var options = _GetOrAddHeadlessDbContextOptions(services);
            configureOptions?.Invoke(options);
            options.RegisterServices(services);

            services.AddOptions<TenantWriteGuardOptions>();
            services.TryAddScoped<HeadlessDbContextServices>();
            services.TryAddScoped<IHeadlessSaveChangesPipeline, HeadlessSaveChangesPipeline>();
            services.TryAddScoped<IHeadlessAuditPersistence, HeadlessAuditPersistence>();
            services.TryAddScoped<IHeadlessMessageDispatcher, ThrowHeadlessMessageDispatcher>();
            services.TryAddSingleton<ITenantWriteGuardBypass, TenantWriteGuardBypass>();

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock, Clock>();
            services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
            services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            // Removes NullCurrentTenant fallback; preserves consumer-supplied ICurrentTenant.
            services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
            services.TryAddSingleton<ICurrentUser, NullCurrentUser>();
            services.TryAddSingleton<ICorrelationIdProvider, ActivityCorrelationIdProvider>();
            services.ReplaceCompiledQueryCacheKeyGenerator();

            return services;
        }

        public IServiceCollection AddHeadlessTenantWriteGuard(Action<TenantWriteGuardOptions>? configure = null)
        {
            services.AddHeadlessDbContextServices();
            services.Configure<TenantWriteGuardOptions>(options =>
            {
                options.IsEnabled = true;
                configure?.Invoke(options);
            });

            return services;
        }

        public IServiceCollection AddHeadlessMessageDispatcher<TDispatcher>(
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
            where TDispatcher : class, IHeadlessMessageDispatcher
        {
            services.TryAdd(ServiceDescriptor.Describe(typeof(TDispatcher), typeof(TDispatcher), lifetime));
            services.Replace(
                ServiceDescriptor.Describe(
                    typeof(IHeadlessMessageDispatcher),
                    provider => provider.GetRequiredService<TDispatcher>(),
                    lifetime
                )
            );

            return services;
        }

        public IServiceCollection AddHeadlessMessageDispatcher(
            Func<IServiceProvider, IHeadlessMessageDispatcher> implementationFactory,
            ServiceLifetime lifetime = ServiceLifetime.Scoped
        )
        {
            Argument.IsNotNull(implementationFactory);

            services.Replace(
                ServiceDescriptor.Describe(typeof(IHeadlessMessageDispatcher), implementationFactory, lifetime)
            );

            return services;
        }

        public IServiceCollection AddHeadlessMessageDispatcher(IHeadlessMessageDispatcher dispatcher)
        {
            Argument.IsNotNull(dispatcher);

            services.Replace(ServiceDescriptor.Singleton(dispatcher));

            return services;
        }
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

/// <summary>Records tenant posture for Headless Entity Framework services.</summary>
public sealed class HeadlessEntityFrameworkTenancyBuilder
{
    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessEntityFrameworkTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Enables the EF tenant write guard.</summary>
    /// <param name="configure">Optional write guard options.</param>
    /// <returns>The same Entity Framework tenancy builder.</returns>
    public HeadlessEntityFrameworkTenancyBuilder GuardTenantWrites(
        Action<TenantWriteGuardOptions>? configure = null
    )
    {
        _builder.Services.AddHeadlessTenantWriteGuard(configure);
        _builder.RecordSeam(
            "EntityFramework",
            TenantPostureStatuses.Guarded,
            "guard-tenant-writes",
            "ef-owned-bypass"
        );

        return this;
    }
}
