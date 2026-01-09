// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Settings;

[PublicAPI]
public static class EntityFrameworkSettingsSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSettingsManagementDbContextStorage(
            Action<DbContextOptionsBuilder> setupAction
        )
        {
            services.AddPooledDbContextFactory<SettingsDbContext>(setupAction);
            services.AddSettingsManagementDbContextStorage<SettingsDbContext>();

            return services;
        }

        public IServiceCollection AddSettingsManagementDbContextStorage(
            Action<IServiceProvider, DbContextOptionsBuilder> setupAction
        )
        {
            services.AddPooledDbContextFactory<SettingsDbContext>(setupAction);
            services.AddSettingsManagementDbContextStorage<SettingsDbContext>();

            return services;
        }

        public IServiceCollection AddSettingsManagementDbContextStorage<TContext>()
            where TContext : DbContext, ISettingsDbContext
        {
            services.AddSingleton<ISettingValueRecordRepository, EfSettingValueRecordRepository<TContext>>();
            services.AddSingleton<ISettingDefinitionRecordRepository, EfSettingDefinitionRecordRepository<TContext>>();

            return services;
        }
    }
}
