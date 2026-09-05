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

/// <summary>SQL Server-specific configuration for the Jobs Entity Framework persistence provider.</summary>
[PublicAPI]
public static class SetupSqlServerJobsEntityFramework
{
    /// <summary>
    /// The GUID ordering every SQL Server-backed Jobs row is keyed with — the single place this package declares it.
    /// Consumed by <see cref="SqlServerJobsClaimStrategy{TDbContext,TTimeJob,TCronJob}"/> through keyed injection and
    /// by the shared EF persistence provider (occurrence materialization) through the builder. UUIDv7 would fragment
    /// the clustered <c>uniqueidentifier</c> primary keys, because SQL Server sorts the bytes it puts its timestamp in
    /// last.
    /// </summary>
    internal const SequentialGuidType GuidGeneratorKey = SequentialGuidType.SqlServer;

    extension(JobsOptionsBuilder<TimeJobEntity, CronJobEntity> builder)
    {
        /// <summary>
        /// Stores jobs in the registered application context and configures SQL Server claims,
        /// cluster membership, and EF commit coordination against the same database.
        /// </summary>
        /// <remarks>
        /// Register the application context first. The context must expose a public constructor accepting
        /// only DbContextOptions&lt;TContext&gt;. Select the advanced UseEntityFramework path when coordination
        /// is already configured separately. This method does not create the application schema.
        /// </remarks>
        public JobsOptionsBuilder<TimeJobEntity, CronJobEntity> UseSqlServer<TContext>(
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
                ef.UseSqlServerClaims();
                ef.ConfigureServices += services =>
                {
                    services.AddEntityFrameworkCommitCoordination<TContext>();
                    services.AddHeadlessCoordination(coordination =>
                    {
                        coordination.Configure(configureCoordination);
                        coordination.UseSqlServer(
                            (options, provider) =>
                            {
                                // Options configuration is synchronous; DbContext supports synchronous disposal.
#pragma warning disable MA0045
                                using var scope = provider.CreateScope();
#pragma warning restore MA0045
                                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                                if (!context.Database.IsSqlServer())
                                {
                                    throw new InvalidOperationException(
                                        $"Jobs UseSqlServer<{typeof(TContext).Name}> requires a SQL Server application DbContext."
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
        /// Uses SQL Server atomic, read-past claims for the Jobs Entity Framework persistence provider.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UseSqlServerClaims()
        {
            Argument.IsNotNull(builder);
            builder.UseClaimStrategy(typeof(SqlServerJobsClaimStrategy<,,>), GuidGeneratorKey);
            return builder;
        }
    }
}
