using Headless.Jobs.DbContextFactory;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;
using Headless.Jobs.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

            services.AddSingleton<
                IJobPersistenceProvider<TTimeJob, TCronJob>,
                JobsEfCorePersistenceProvider<TContext, TTimeJob, TCronJob>
            >();
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
            services.AddSingleton<
                IJobPersistenceProvider<TTimeJob, TCronJob>,
                JobsEfCorePersistenceProvider<TContext, TTimeJob, TCronJob>
            >();
        };
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
