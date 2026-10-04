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
    /// idempotency reservation table. Names follow the <paramref name="context"/>'s database provider: snake_case on
    /// PostgreSQL, PascalCase elsewhere, matching the style passed to the Jobs entity configurations.
    /// </summary>
    /// <remarks>
    /// Call at the end of OnModelCreating after applying Jobs configurations and all consumer mappings when
    /// using ConfigurationType.IgnoreModelCustomizer. The built-in Jobs model customizer performs this step automatically.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="context"/> is null.</exception>
    public static ModelBuilder FinalizeJobsModel<TTimeJob>(this ModelBuilder builder, DbContext context)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(context);

        JobsKeyedModelConfiguration.Configure<TTimeJob>(builder, context);
        // The reservation table is not generic, so this is the one place a consumer-managed model maps it. It takes
        // its schema from the same option as every other Jobs table so an override cannot strand it in the default
        // schema.
        JobsIdempotencyModelConfiguration.Configure(
            builder,
            context.GetService<JobsStorageOptions>().Schema,
            JobsStorageNaming.StyleOf(context),
            JobsContractCollation.TryResolve(context.Database.ProviderName)
        );
        return builder;
    }
}
