// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.CommitCoordination;
using Headless.Coordination;
using Headless.Jobs.Customizer;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>PostgreSQL-specific configuration for the Jobs Entity Framework persistence provider.</summary>
[PublicAPI]
public static class SetupPostgreSqlJobsEntityFramework
{
    /// <summary>
    /// The GUID ordering every PostgreSQL-backed Jobs row is keyed with — the single place this package declares it.
    /// Consumed by <see cref="PostgreSqlJobsClaimStrategy{TDbContext,TTimeJob,TCronJob}"/> through keyed injection and
    /// by the shared EF persistence provider (occurrence materialization) through the builder. PostgreSQL compares
    /// <c>uuid</c> in plain byte order, so UUIDv7's leading timestamp keeps index inserts at the right edge.
    /// </summary>
    internal const SequentialGuidType GuidGeneratorKey = SequentialGuidType.Version7;

    extension(JobsOptionsBuilder<TimeJobEntity, CronJobEntity> builder)
    {
        /// <summary>
        /// Stores jobs in the registered application context and configures PostgreSQL claims,
        /// cluster membership, and EF commit coordination against the same database.
        /// </summary>
        /// <remarks>
        /// Register the application context first. The context must expose a public constructor accepting
        /// only DbContextOptions&lt;TContext&gt;. Select the advanced UseEntityFramework path when coordination
        /// is already configured separately. This method does not create the application schema.
        /// </remarks>
        public JobsOptionsBuilder<TimeJobEntity, CronJobEntity> UsePostgreSql<TContext>(
            Action<CoordinationOptions> configureCoordination,
            ConfigurationType modelConfiguration = ConfigurationType.UseModelCustomizer
        )
            where TContext : DbContext
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(configureCoordination);

            return builder.UseEntityFramework(ef =>
            {
                ef.UseApplicationDbContext<TContext>(modelConfiguration);
                ef.UsePostgreSqlClaims();
                ef.ConfigureServices += services =>
                {
                    services.AddEntityFrameworkCommitCoordination<TContext>();
                    services.AddHeadlessCoordination(coordination =>
                    {
                        coordination.Configure(configureCoordination);
                        coordination.UsePostgreSql(
                            (options, provider) =>
                            {
                                // Options configuration is synchronous; DbContext supports synchronous disposal.
#pragma warning disable MA0045
                                using var scope = provider.CreateScope();
#pragma warning restore MA0045
                                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                                if (!context.Database.IsNpgsql())
                                {
                                    throw new InvalidOperationException(
                                        $"Jobs UsePostgreSql<{typeof(TContext).Name}> requires a PostgreSQL application DbContext."
                                    );
                                }

                                options.ConnectionString = context.Database.GetConnectionString();
                            }
                        );
                    });
                };
            });
        }
    }

    extension<TTimeJob, TCronJob>(JobsEfCoreOptionBuilder<TTimeJob, TCronJob> builder)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        /// <summary>
        /// Uses PostgreSQL atomic, skip-locked claims for the Jobs Entity Framework persistence provider.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UsePostgreSqlClaims()
        {
            Argument.IsNotNull(builder);
            builder.UseClaimStrategy(typeof(PostgreSqlJobsClaimStrategy<,,>), GuidGeneratorKey);
            return builder;
        }
    }
}
