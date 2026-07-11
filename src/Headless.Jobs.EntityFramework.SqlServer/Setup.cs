// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Infrastructure;

namespace Headless.Jobs;

/// <summary>SQL Server-specific configuration for the Jobs Entity Framework persistence provider.</summary>
[PublicAPI]
public static class SetupSqlServerJobsEntityFramework
{
    extension<TTimeJob, TCronJob>(JobsEfCoreOptionBuilder<TTimeJob, TCronJob> builder)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        /// <summary>
        /// Uses SQL Server atomic, read-past claims for the Jobs Entity Framework persistence provider.
        /// </summary>
        public JobsEfCoreOptionBuilder<TTimeJob, TCronJob> UseSqlServerClaims()
        {
            Argument.IsNotNull(builder);
            builder.UseClaimStrategy(typeof(SqlServerJobsClaimStrategy<,,>));
            return builder;
        }
    }
}
