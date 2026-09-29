// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Jobs;
using Headless.Jobs.Customizer;
using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Builds the Jobs model a consumer-hosted context gets from the Jobs model customizer for one EF provider, without a
/// database, so the storage names it produces can be asserted per provider.
/// </summary>
public static class JobsModelNaming
{
    /// <summary>Builds the design-time model, which keeps the check constraints the runtime model drops.</summary>
    public static IModel BuildDesignTimeModel(Action<DbContextOptionsBuilder> useProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobsStorageOptions { Schema = HeadlessStorageDefaults.Schema });
        var provider = services.BuildServiceProvider();

        var builder = new DbContextOptionsBuilder<NamingProbeDbContext>();
        useProvider(builder);
        builder
            .ReplaceService<IModelCustomizer, JobsModelCustomizer<TimeJobEntity, CronJobEntity>>()
            .UseApplicationServiceProvider(provider)
            // EF caches the model per context type; a private internal provider keeps each provider's model apart.
            .EnableServiceProviderCaching(false);

        using var context = new NamingProbeDbContext(builder.Options);

        return context.GetService<IDesignTimeModel>().Model;
    }

    /// <summary>The names of every index on <paramref name="entity" />.</summary>
    public static IEnumerable<string?> IndexNames(this IEntityType entity) =>
        entity.GetIndexes().Select(index => index.GetDatabaseName());

    /// <summary>The column <paramref name="property" /> maps to in the entity's own table.</summary>
    public static string? ColumnName(this IEntityType entity, string property) =>
        entity
            .FindProperty(property)!
            .GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()));

    private sealed class NamingProbeDbContext(DbContextOptions<NamingProbeDbContext> options) : DbContext(options);
}
