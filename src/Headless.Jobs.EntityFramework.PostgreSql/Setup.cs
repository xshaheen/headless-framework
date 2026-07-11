// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;

namespace Headless.Jobs;

/// <summary>PostgreSQL-specific configuration for the Jobs Entity Framework persistence provider.</summary>
[PublicAPI]
public static class SetupPostgreSqlJobsEntityFramework
{
    extension<TTimeJob, TCronJob>(JobsEfCoreOptionBuilder<TTimeJob, TCronJob> builder)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        /// <summary>
        /// Uses PostgreSQL atomic, skip-locked claims for the Jobs Entity Framework persistence provider.
        /// </summary>
        public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UsePostgreSqlClaims()
        {
            Argument.IsNotNull(builder);
            builder.UseClaimStrategy(typeof(PostgreSqlJobsClaimStrategy<,,>));
            return builder;
        }
    }
}
