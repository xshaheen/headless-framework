// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Permissions.Storage.EntityFramework;

[PublicAPI]
public static class EntityFrameworkPermissionsSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPermissionsManagementDbContextStorage(
            Action<DbContextOptionsBuilder> setupAction,
            Action<PermissionsStorageOptions>? configureStorage = null
        )
        {
            services.AddPooledDbContextFactory<PermissionsDbContext>(options =>
            {
                setupAction(options);
                options.ReplaceService<IModelCacheKeyFactory, PermissionsStorageModelCacheKeyFactory>();
            });
            services.AddPermissionsManagementDbContextStorage<PermissionsDbContext>(configureStorage);

            return services;
        }

        public IServiceCollection AddPermissionsManagementDbContextStorage(
            Action<IServiceProvider, DbContextOptionsBuilder> setupAction,
            Action<PermissionsStorageOptions>? configureStorage = null
        )
        {
            services.AddPooledDbContextFactory<PermissionsDbContext>((provider, options) =>
            {
                setupAction(provider, options);
                options.ReplaceService<IModelCacheKeyFactory, PermissionsStorageModelCacheKeyFactory>();
            });
            services.AddPermissionsManagementDbContextStorage<PermissionsDbContext>(configureStorage);

            return services;
        }

        public IServiceCollection AddPermissionsManagementDbContextStorage<TContext>(
            Action<PermissionsStorageOptions>? configureStorage = null
        )
            where TContext : DbContext, IPermissionsDbContext
        {
            services.Configure<PermissionsStorageOptions, PermissionsStorageOptionsValidator>(configureStorage);
            services.AddSingleton<IPermissionGrantRepository, EfPermissionGrantRepository<TContext>>();

            services.AddSingleton<
                IPermissionDefinitionRecordRepository,
                EfPermissionDefinitionRecordRepository<TContext>
            >();

            return services;
        }
    }
}
