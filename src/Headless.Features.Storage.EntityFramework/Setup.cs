// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features.Storage.EntityFramework;

[PublicAPI]
public static class EntityFrameworkFeaturesSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFeaturesManagementDbContextStorage(
            Action<DbContextOptionsBuilder> setupAction,
            Action<FeaturesStorageOptions>? configureStorage = null
        )
        {
            services.AddPooledDbContextFactory<FeaturesDbContext>(options =>
            {
                setupAction(options);
                options.ReplaceService<IModelCacheKeyFactory, FeaturesStorageModelCacheKeyFactory>();
            });
            return services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>(configureStorage);
        }

        public IServiceCollection AddFeaturesManagementDbContextStorage(
            Action<IServiceProvider, DbContextOptionsBuilder> setupAction,
            Action<FeaturesStorageOptions>? configureStorage = null
        )
        {
            services.AddPooledDbContextFactory<FeaturesDbContext>((provider, options) =>
            {
                setupAction(provider, options);
                options.ReplaceService<IModelCacheKeyFactory, FeaturesStorageModelCacheKeyFactory>();
            });
            return services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>(configureStorage);
        }

        public IServiceCollection AddFeaturesManagementDbContextStorage<TContext>(
            Action<FeaturesStorageOptions>? configureStorage = null
        )
            where TContext : DbContext, IFeaturesDbContext
        {
            services.Configure<FeaturesStorageOptions, FeaturesStorageOptionsValidator>(configureStorage);
            services.AddSingleton<IFeatureValueRecordRepository, EfFeatureValueRecordRecordRepository<TContext>>();
            services.AddSingleton<IFeatureDefinitionRecordRepository, EfFeatureDefinitionRecordRepository<TContext>>();

            return services;
        }
    }
}
