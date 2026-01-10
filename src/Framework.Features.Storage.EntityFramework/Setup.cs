// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Features;

[PublicAPI]
public static class EntityFrameworkSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFeaturesManagementDbContextStorage(Action<DbContextOptionsBuilder> setupAction)
        {
            services.AddPooledDbContextFactory<FeaturesDbContext>(setupAction);
            return services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>();
        }

        public IServiceCollection AddFeaturesManagementDbContextStorage(
            Action<IServiceProvider, DbContextOptionsBuilder> setupAction
        )
        {
            services.AddPooledDbContextFactory<FeaturesDbContext>(setupAction);
            return services.AddFeaturesManagementDbContextStorage<FeaturesDbContext>();
        }

        public IServiceCollection AddFeaturesManagementDbContextStorage<TContext>()
            where TContext : DbContext, IFeaturesDbContext
        {
            services.AddSingleton<IFeatureValueRecordRepository, EfFeatureValueRecordRecordRepository<TContext>>();
            services.AddSingleton<IFeatureDefinitionRecordRepository, EfFeatureDefinitionRecordRepository<TContext>>();

            return services;
        }
    }
}
