// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Features;

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
            services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>(configureStorage);

            return services;
        }

        public IServiceCollection AddFeaturesManagementDbContextStorage(
            Action<IServiceProvider, DbContextOptionsBuilder> setupAction,
            Action<FeaturesStorageOptions>? configureStorage = null
        )
        {
            services.AddPooledDbContextFactory<FeaturesDbContext>(
                (provider, options) =>
                {
                    setupAction(provider, options);
                    options.ReplaceService<IModelCacheKeyFactory, FeaturesStorageModelCacheKeyFactory>();
                }
            );
            services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>(configureStorage);

            return services;
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
