// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Definitions;
using Framework.Features.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Features;

[PublicAPI]
public static class AddFeaturesManagementStorageExtensions
{
    public static IServiceCollection AddFeaturesManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> setupAction
    )
    {
        return services.AddFeaturesManagementEntityFrameworkStorage<FeaturesDbContext>(setupAction);
    }

    public static IServiceCollection AddFeaturesManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> setupAction
    )
    {
        return services.AddFeaturesManagementEntityFrameworkStorage<FeaturesDbContext>(setupAction);
    }

    public static IServiceCollection AddFeaturesManagementEntityFrameworkStorage<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> setupAction
    )
        where TContext : DbContext, IFeaturesDbContext
    {
        services.AddPooledDbContextFactory<TContext>(setupAction);
        services.AddSingleton<IFeatureValueRecordRepository, EfFeatureValueRecordRecordRepository<TContext>>();
        services.AddSingleton<IFeatureDefinitionRecordRepository, EfFeatureDefinitionRecordRepository<TContext>>();

        return services;
    }

    public static IServiceCollection AddFeaturesManagementEntityFrameworkStorage<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> setupAction
    )
        where TContext : DbContext, IFeaturesDbContext
    {
        services.AddPooledDbContextFactory<TContext>(setupAction);
        services.AddSingleton<IFeatureValueRecordRepository, EfFeatureValueRecordRecordRepository<TContext>>();
        services.AddSingleton<IFeatureDefinitionRecordRepository, EfFeatureDefinitionRecordRepository<TContext>>();

        return services;
    }
}
