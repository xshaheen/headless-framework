// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Finalizes Jobs constraints for consumer-managed relational models.</summary>
public static class HeadlessJobsModelBuilderExtensions
{
    /// <summary>
    /// Builds keyed Jobs indexes and check constraints from the final table and column mappings, and maps the
    /// idempotency reservation table and the attempt history tables. Names follow the <paramref name="context"/>'s
    /// database provider: snake_case on PostgreSQL, PascalCase elsewhere, matching the style passed to the Jobs entity
    /// configurations.
    /// </summary>
    /// <remarks>
    /// Call at the end of OnModelCreating after applying Jobs configurations and all consumer mappings when
    /// using ConfigurationType.IgnoreModelCustomizer. The built-in Jobs model customizer performs this step automatically.
    /// A model that skips it records no attempt history: the attempt tables need the run tables already mapped, because
    /// each attempt cascades from its run.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="context"/> is null.</exception>
    public static ModelBuilder FinalizeJobsModel<TTimeJob>(this ModelBuilder builder, DbContext context)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(context);

        var schema = context.GetService<JobsStorageOptions>().Schema;
        var style = JobsStorageNaming.StyleOf(context);

        JobsKeyedModelConfiguration.Configure<TTimeJob>(builder, context);
        // The reservation table is not generic, so this is the one place a consumer-managed model maps it. It takes
        // its schema from the same option as every other Jobs table so an override cannot strand it in the default
        // schema.
        JobsIdempotencyModelConfiguration.Configure(
            builder,
            schema,
            style,
            JobsContractCollation.TryResolve(context.Database.ProviderName)
        );
        // The occurrence type closes over the consumer's cron entity, which this method does not name, so it is read
        // from the mapped model.
        JobsAttemptModelConfiguration.Configure(
            builder,
            typeof(TTimeJob),
            JobsAttemptModelConfiguration.FindOccurrenceType(builder),
            schema,
            style
        );
        return builder;
    }
}
