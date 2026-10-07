// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Coordination;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;
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
        /// cluster membership, and the EF Core unit of work against the same database.
        /// </summary>
        /// <remarks>
        /// Register the application context first. The context must expose a public constructor accepting
        /// only DbContextOptions&lt;TContext&gt;; a HeadlessDbContext qualifies, pooled or not. Select the advanced
        /// UseEntityFramework path when coordination
        /// is already configured separately. This method does not create the application schema.
        /// </remarks>
        /// <param name="configureCoordination">
        /// Configures the coordination this method registers, for example
        /// <c>coordination =&gt; coordination.Configure(options =&gt; options.ClusterName = "orders")</c>, or
        /// <c>DisableMembershipHeartbeat()</c> on a test host. The provider is selected here; do not call a
        /// <c>Use*</c> provider method on it.
        /// </param>
        /// <param name="modelConfiguration">How the jobs model is added to the application context.</param>
        public JobsOptionsBuilder<TimeJobEntity, CronJobEntity> UseSqlServer<TContext>(
            Action<HeadlessCoordinationSetupBuilder> configureCoordination,
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
                    services.AddEntityFrameworkUnitOfWork();
                    services.AddHeadlessCoordination(coordination =>
                    {
                        configureCoordination(coordination);
                        coordination.UseSqlServer(
                            (options, provider) =>
                            {
                                using var scope = provider.CreateScope();
                                var context = scope.ServiceProvider.GetRequiredService<TContext>();
                                if (!context.Database.IsSqlServer())
                                {
                                    throw new InvalidOperationException(
                                        $"Jobs UseSqlServer<{typeof(TContext).Name}> requires a SQL Server application DbContext."
                                    );
                                }

                                // The connection can hold credentials a connection string cannot carry; the copy
                                // refuses one that authenticates with them rather than failing at the first login.
                                // GetConnectionString() is the string the context was configured from, which keeps
                                // the password a pooled, already-opened connection lost. A context without a
                                // connection string leaves it empty, which the coordination options validator then
                                // reports at startup.
                                options.ConnectionString = context.Database.GetDbConnection()
                                    is SqlConnection connection
                                    ? connection.GetReusableConnectionString(context.Database.GetConnectionString())
                                    : string.Empty;
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
