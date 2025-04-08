// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Features;

[PublicAPI]
public static class AddFeaturesManagementStorageExtensions
{
    public static IServiceCollection AddFeaturesManagementDbContextStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> setupAction
    )
    {
        services.AddPooledDbContextFactory<FeaturesDbContext>(setupAction);
        return services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>();
    }

    public static IServiceCollection AddFeaturesManagementDbContextStorage(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> setupAction
    )
    {
        services.AddPooledDbContextFactory<FeaturesDbContext>(setupAction);
        return services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>();
    }

    public static IServiceCollection AddFeaturesManagementDbContextStorage<TContext>(this IServiceCollection services)
        where TContext : DbContext, IFeaturesDbContext
    {
        services.AddSingleton<IFeatureValueRecordRepository, EfFeatureValueRecordRecordRepository<TContext>>();
        services.AddSingleton<IFeatureDefinitionRecordRepository, EfFeatureDefinitionRecordRepository<TContext>>();

        return services;
    }
}
