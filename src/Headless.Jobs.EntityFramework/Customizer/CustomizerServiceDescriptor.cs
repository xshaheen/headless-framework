// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Customizer;

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

                return (DbContextOptions<TContext>)serviceDescriptor.ImplementationFactory(provider);
            }

            services.TryAddSingleton<IDbContextFactory<TContext>>(provider => new PooledDbContextFactory<TContext>(
                resolveOptionsTemplate(provider),
                builder.PoolSize
            ));

            _AddPersistenceProviderCore<TContext, TTimeJob, TCronJob>(services, builder, resolveOptionsTemplate);
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
            services.AddDbContext<TContext>(optionsAction);

            // Builds the options template the way the pooled factory does (apply the consumer's options action, then
            // bind the app service provider). Shared by the pooled factory and the coordinated-write path, which
            // clones this template and swaps only the connection.
            DbContextOptions<TContext> resolveOptionsTemplate(IServiceProvider sp)
            {
                var optionsBuilder = new DbContextOptionsBuilder<TContext>();
                optionsAction.Invoke(optionsBuilder);
                optionsBuilder.UseApplicationServiceProvider(sp);
                return optionsBuilder.Options;
            }

            services.TryAddSingleton<IDbContextFactory<TContext>>(sp => new PooledDbContextFactory<TContext>(
                resolveOptionsTemplate(sp),
                builder.PoolSize
            ));

            _AddPersistenceProviderCore<TContext, TTimeJob, TCronJob>(services, builder, resolveOptionsTemplate);
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
        // Fail loud at DI-build time when the context cannot back coordinated writes, rather than at first
        // coordinated write where the provider's static factory would surface it as a TypeInitializationException.
        CoordinatedWriteContextFactory.RequireOptionsConstructor<TContext>();

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
                    var casStrategy = ActivatorUtilities.CreateInstance(provider, casStrategyType);
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
                provider.GetRequiredService<IJobsOwnerIdentity>(),
                provider.GetRequiredService<SchedulerOptionsBuilder>(),
                provider.GetService<ICache>(),
                provider.GetRequiredService<IJobsClaimStrategy<TTimeJob, TCronJob>>(),
                provider.GetRequiredService<ILogger<JobsEfCorePersistenceProvider<TContext, TTimeJob, TCronJob>>>()
            )
        );
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
