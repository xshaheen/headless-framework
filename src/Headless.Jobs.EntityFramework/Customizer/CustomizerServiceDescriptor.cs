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

public static class ServiceBuilder
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
                var originalDescriptor = services.FirstOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(DbContextOptions<TContext>)
                );

                if (originalDescriptor == null)
                {
                    throw new InvalidOperationException(
                        $"Job: Cannot use UseModelCustomizer with empty {typeof(TContext).Name} configurations"
                    );
                }

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

            services.TryAddSingleton<IDbContextFactory<TContext>>(provider =>
            {
                var serviceDescriptor = services.FirstOrDefault(d =>
                    d.ServiceType == typeof(DbContextOptions<TContext>)
                );

                if (serviceDescriptor?.ImplementationFactory == null)
                {
                    throw new InvalidOperationException($"Cannot resolve DbContextOptions<{typeof(TContext).Name}>");
                }

                var options = (DbContextOptions<TContext>)serviceDescriptor.ImplementationFactory(provider);

                return new PooledDbContextFactory<TContext>(options, builder.PoolSize);
            });

            _AddPersistenceProviderCore<TContext, TTimeJob, TCronJob>(services);
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
            services.TryAddSingleton<IDbContextFactory<TContext>>(sp =>
            {
                var optionsBuilder = new DbContextOptionsBuilder<TContext>();
                optionsAction.Invoke(optionsBuilder);
                optionsBuilder.UseApplicationServiceProvider(sp);
                return new PooledDbContextFactory<TContext>(optionsBuilder.Options, builder.PoolSize);
            });
            _AddPersistenceProviderCore<TContext, TTimeJob, TCronJob>(services);
        };
    }

    private static void _AddPersistenceProviderCore<TContext, TTimeJob, TCronJob>(IServiceCollection services)
        where TContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // ICache is resolved with GetService (optional): cron-expression caching is enabled only when the host
        // application registers a default Headless.Caching provider; otherwise Jobs reads cron expressions from the DB.
        services.AddSingleton<IJobPersistenceProvider<TTimeJob, TCronJob>>(
            provider => new JobsEfCorePersistenceProvider<TContext, TTimeJob, TCronJob>(
                provider.GetRequiredService<IDbContextFactory<TContext>>(),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<IJobsOwnerIdentity>(),
                provider.GetService<ICache>(),
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
