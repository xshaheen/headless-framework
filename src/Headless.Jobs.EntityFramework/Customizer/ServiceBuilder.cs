// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Headless.Jobs.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

internal static class ServiceBuilder
{
    internal static void UseApplicationDbContext<TContext, TTimeJob, TCronJob>(
        JobsEfCoreOptionBuilder<TTimeJob, TCronJob> builder,
        ConfigurationType configurationType
    )
        where TContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        builder.ConfigureServices = (services) =>
        {
            if (configurationType == ConfigurationType.UseModelCustomizer)
            {
                var originalDescriptor =
                    services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>))
                    ?? throw new InvalidOperationException(
                        $"Job: Cannot use UseModelCustomizer with empty {typeof(TContext).Name} configurations"
                    );

                if (originalDescriptor.ImplementationFactory == null)
                {
                    throw new InvalidOperationException(
                        $"Job: DbContextOptions<{typeof(TContext).Name}> must be registered with an ImplementationFactory"
                    );
                }

                var newDescriptor = new ServiceDescriptor(
                    typeof(DbContextOptions<TContext>),
                    provider =>
                        _UpdateDbContextOptionsService<TContext, TTimeJob, TCronJob>(
                            provider,
                            originalDescriptor.ImplementationFactory
                        ),
                    originalDescriptor.Lifetime
                );

                services.Remove(originalDescriptor);
                services.Add(newDescriptor);
            }

            // Resolves the registered DbContextOptions<TContext> template (customizer-applied when UseModelCustomizer
            // replaced the descriptor above). Shared by the pooled factory and the coordinated-write path, which
            // clones this template and swaps only the connection.
            DbContextOptions<TContext> resolveOptionsTemplate(IServiceProvider provider)
            {
                var serviceDescriptor = services.FirstOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<TContext>)
                );

                if (serviceDescriptor?.ImplementationFactory == null)
                {
                    throw new InvalidOperationException($"Cannot resolve DbContextOptions<{typeof(TContext).Name}>");
                }

                // The template lives as long as the host. Scoped options (the AddDbContext and AddHeadlessDbContext
                // default) build from scoped configuration, which the root provider refuses under scope validation,
                // so they resolve once from a scope Jobs holds for the host's lifetime.
                if (serviceDescriptor.Lifetime == ServiceLifetime.Singleton)
                {
                    return (DbContextOptions<TContext>)serviceDescriptor.ImplementationFactory(provider);
                }

                var scopedTemplate =
                    (DbContextOptions<TContext>)
                        serviceDescriptor.ImplementationFactory(
                            provider.GetRequiredService<OptionsTemplateScope>().Services
                        );

                // EF stamps options with the provider that built them, and a HeadlessDbContext adopts a non-root
                // stamp as its own scope. Every Jobs context would then share that host-lifetime scope, so its save
                // pipeline's scoped collaborators would be used concurrently and never disposed. Stamped with the
                // root, each context opens and disposes a private scope, as it does from singleton options.
                return new DbContextOptionsBuilder<TContext>(scopedTemplate)
                    .UseApplicationServiceProvider(provider)
                    .Options;
            }

            services.TryAddSingleton<OptionsTemplateScope>();

            services.TryAddSingleton<IDbContextFactory<TContext>>(provider => new PooledDbContextFactory<TContext>(
                resolveOptionsTemplate(provider),
                builder.PoolSize
            ));

            _AddPersistenceProviderCore(services, builder, resolveOptionsTemplate);
        };
    }

    internal static void UseJobsDbContext<TContext, TTimeJob, TCronJob>(
        JobsEfCoreOptionBuilder<TTimeJob, TCronJob> builder,
        Action<DbContextOptionsBuilder> optionsAction
    )
        where TContext : JobsDbContext<TTimeJob, TCronJob>
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        builder.ConfigureServices = services =>
        {
            services.AddDbContext<TContext>(options =>
            {
                optionsAction(options);
                options.ReplaceService<IModelCustomizer, JobsModelCustomizer<TTimeJob, TCronJob>>();
            });

            // Builds the options template the way the pooled factory does (apply the consumer's options action, then
            // bind the app service provider). Shared by the pooled factory and the coordinated-write path, which
            // clones this template and swaps only the connection.
            DbContextOptions<TContext> resolveOptionsTemplate(IServiceProvider sp)
            {
                var optionsBuilder = new DbContextOptionsBuilder<TContext>();
                optionsAction.Invoke(optionsBuilder);
                optionsBuilder.ReplaceService<IModelCustomizer, JobsModelCustomizer<TTimeJob, TCronJob>>();
                optionsBuilder.UseApplicationServiceProvider(sp);
                return optionsBuilder.Options;
            }

            services.TryAddSingleton<IDbContextFactory<TContext>>(sp => new PooledDbContextFactory<TContext>(
                resolveOptionsTemplate(sp),
                builder.PoolSize
            ));

            _AddPersistenceProviderCore(services, builder, resolveOptionsTemplate);
        };
    }

    private static void _AddPersistenceProviderCore<TContext, TTimeJob, TCronJob>(
        IServiceCollection services,
        JobsEfCoreOptionBuilder<TTimeJob, TCronJob> builder,
        Func<IServiceProvider, DbContextOptions<TContext>> coordinatedWriteOptionsFactory
    )
        where TContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // Defensive: this package RESOLVES TimeProvider, so it must also guarantee one exists. Without this,
        // installing the package standalone (no ServiceDefaults, no sibling that happens to register it) throws
        // 'No service for type TimeProvider' at resolve time.
        services.TryAddSingleton(TimeProvider.System);
        // The persistence provider and claim strategies are singletons over IDbContextFactory<TContext>. Jobs registers
        // a pooled singleton factory with TryAdd, so an application factory registered first wins and must be a singleton
        // too, or those singletons keep one scoped or transient factory for the life of the host.
        services.RequireSingletonService<IDbContextFactory<TContext>>(
            requiredBy: "Headless Jobs EF persistence",
            remedy: "Remove the application's IDbContextFactory<TContext> registration so Jobs registers its pooled singleton factory, or register it at the default singleton lifetime: AddDbContextFactory<TContext>() or AddPooledDbContextFactory<TContext>() for a plain DbContext, or AddHeadlessDbContext<TContext>() or AddHeadlessDbContextPool<TContext>() for a HeadlessDbContext."
        );
        services.AddHeadlessGuidGenerator();
        // Fail loud at DI-build time when the context cannot back coordinated writes, rather than at first
        // coordinated write where the provider's static factory would surface it as a TypeInitializationException.
        CoordinatedWriteContextFactory.RequireOptionsConstructor<TContext>();

        // The provider package that installs a native claim strategy also declares that backend's GUID ordering, so
        // every EF write path stamps ids with the generator the native strategy already resolves by key: SQL Server
        // gets the comb its clustered `uniqueidentifier` key needs, PostgreSQL keeps UUIDv7. Generic EF registers no
        // backend key and keeps the unkeyed default.
        IGuidGenerator resolveGuidGenerator(IServiceProvider provider)
        {
            return builder.GuidGeneratorKey is { } guidGeneratorKey
                ? provider.GetRequiredKeyedService<IGuidGenerator>(guidGeneratorKey)
                : provider.GetRequiredService<IGuidGenerator>();
        }

        var claimStrategyServiceType = typeof(IJobsClaimStrategy<TTimeJob, TCronJob>);
        var claimStrategyImplementationType =
            builder.ClaimStrategyTypeDefinition?.MakeGenericType(typeof(TContext), typeof(TTimeJob), typeof(TCronJob))
            ?? typeof(EfCoreCasJobsClaimStrategy<TContext, TTimeJob, TCronJob>);

        if (!claimStrategyServiceType.IsAssignableFrom(claimStrategyImplementationType))
        {
            throw new InvalidOperationException(
                $"Configured Jobs EF claim strategy {claimStrategyImplementationType.FullName} must implement "
                    + $"{claimStrategyServiceType.FullName}."
            );
        }

        if (
            claimStrategyImplementationType.IsGenericType
            && claimStrategyImplementationType.GetGenericTypeDefinition() == typeof(EfCoreCasJobsClaimStrategy<,,>)
        )
        {
            services.AddSingleton(
                claimStrategyServiceType,
                provider => ActivatorUtilities.CreateInstance(provider, claimStrategyImplementationType)
            );
        }
        else
        {
            services.AddSingleton(
                claimStrategyImplementationType,
                provider => ActivatorUtilities.CreateInstance(provider, claimStrategyImplementationType)
            );
            services.AddSingleton(
                claimStrategyServiceType,
                provider =>
                {
                    var nativeStrategy = provider.GetRequiredService(claimStrategyImplementationType);
                    var casStrategyType = typeof(EfCoreCasJobsClaimStrategy<,,>).MakeGenericType(
                        typeof(TContext),
                        typeof(TTimeJob),
                        typeof(TCronJob)
                    );
                    // The CAS strategy is the fallback half of the compatible pair, so it writes into the same
                    // backend as the native half and must key its rows the same way.
                    var casStrategy = ActivatorUtilities.CreateInstance(
                        provider,
                        casStrategyType,
                        resolveGuidGenerator(provider)
                    );
                    var compatibleStrategyType = typeof(CompatibleJobsClaimStrategy<,,>).MakeGenericType(
                        typeof(TContext),
                        typeof(TTimeJob),
                        typeof(TCronJob)
                    );
                    return ActivatorUtilities.CreateInstance(
                        provider,
                        compatibleStrategyType,
                        nativeStrategy,
                        casStrategy
                    );
                }
            );
        }

        // ICache is resolved with GetService (optional): cron-expression caching is enabled only when the host
        // application registers a default Headless.Caching provider; otherwise Jobs reads cron expressions from the DB.
        services.AddSingleton<IJobPersistenceProvider<TTimeJob, TCronJob>>(
            provider => new JobsEfCorePersistenceProvider<TContext, TTimeJob, TCronJob>(
                provider.GetRequiredService<IDbContextFactory<TContext>>(),
                coordinatedWriteOptionsFactory(provider),
                provider.GetRequiredService<TimeProvider>(),
                resolveGuidGenerator(provider),
                provider.GetRequiredService<IJobsOwnerIdentity>(),
                provider.GetRequiredService<SchedulerOptionsBuilder>(),
                provider.GetService<ICache>(),
                provider.GetRequiredService<IJobsClaimStrategy<TTimeJob, TCronJob>>(),
                provider.GetRequiredService<ILogger<JobsEfCorePersistenceProvider<TContext, TTimeJob, TCronJob>>>(),
                provider.GetService<JobsRunFilter>(),
                provider.GetService<JobsClusterConcurrency>()
            )
        );
    }

    /// <summary>
    /// The scope the application context's scoped options template resolves from. The root provider disposes it with
    /// the host, so the scoped services the template captured live exactly as long as the template.
    /// </summary>
    private sealed class OptionsTemplateScope(IServiceScopeFactory scopeFactory) : IDisposable, IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope = scopeFactory.CreateAsyncScope();

        public IServiceProvider Services => _scope.ServiceProvider;

        public void Dispose() => _scope.Dispose();

        public ValueTask DisposeAsync() => _scope.DisposeAsync();
    }

    private static DbContextOptions<TContext> _UpdateDbContextOptionsService<TContext, TTimeJob, TCronJob>(
        IServiceProvider serviceProvider,
        Func<IServiceProvider, object> oldFactory
    )
        where TContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var factory = (DbContextOptions<TContext>)oldFactory(serviceProvider);

        return new DbContextOptionsBuilder<TContext>(factory)
            .ReplaceService<IModelCustomizer, JobsModelCustomizer<TTimeJob, TCronJob>>()
            .Options;
    }
}
