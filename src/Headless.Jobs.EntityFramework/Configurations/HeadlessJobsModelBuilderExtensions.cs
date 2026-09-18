// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs;
using Headless.Jobs.Configurations;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Finalizes Jobs constraints for consumer-managed relational models.</summary>
public static class HeadlessJobsModelBuilderExtensions
{
    /// <summary>Builds keyed Jobs indexes and check constraints from the final table and column mappings.</summary>
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
        // its schema from the same option as every other Jobs table so an override cannot strand it in "jobs".
        JobsIdempotencyModelConfiguration.Configure(
            builder,
            context.GetService<JobsStorageOptions>().Schema,
            JobsContractCollation.TryResolve(context.Database.ProviderName)
        );
        return builder;
    }
}
