// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Jobs.Entities;

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
