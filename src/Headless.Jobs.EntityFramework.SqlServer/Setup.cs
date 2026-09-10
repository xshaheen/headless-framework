// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Entities;

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
