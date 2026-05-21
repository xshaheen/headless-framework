// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Settings.Storage.EntityFramework;

[PublicAPI]
public static class EntityFrameworkSettingsSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSettingsManagementDbContextStorage(
            Action<DbContextOptionsBuilder> setupAction,
            Action<SettingsStorageOptions>? configureStorage = null
        )
        {
            services.AddPooledDbContextFactory<SettingsDbContext>(options =>
            {
                setupAction(options);
                options.ReplaceService<IModelCacheKeyFactory, SettingsStorageModelCacheKeyFactory>();
            });
            services.AddSettingsManagementDbContextStorage<SettingsDbContext>(configureStorage);

            return services;
        }

        public IServiceCollection AddSettingsManagementDbContextStorage(
            Action<IServiceProvider, DbContextOptionsBuilder> setupAction,
            Action<SettingsStorageOptions>? configureStorage = null
        )
        {
            services.AddPooledDbContextFactory<SettingsDbContext>((provider, options) =>
            {
                setupAction(provider, options);
                options.ReplaceService<IModelCacheKeyFactory, SettingsStorageModelCacheKeyFactory>();
            });
            services.AddSettingsManagementDbContextStorage<SettingsDbContext>(configureStorage);

            return services;
        }

        public IServiceCollection AddSettingsManagementDbContextStorage<TContext>(
            Action<SettingsStorageOptions>? configureStorage = null
        )
            where TContext : DbContext, ISettingsDbContext
        {
            services.Configure<SettingsStorageOptions, SettingsStorageOptionsValidator>(configureStorage);
            services.AddSingleton<ISettingValueRecordRepository, EfSettingValueRecordRepository<TContext>>();
            services.AddSingleton<ISettingDefinitionRecordRepository, EfSettingDefinitionRecordRepository<TContext>>();

            return services;
        }
    }
}
