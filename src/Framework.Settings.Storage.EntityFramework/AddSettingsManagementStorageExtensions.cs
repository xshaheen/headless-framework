// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Settings;

[PublicAPI]
public static class AddSettingsManagementStorageExtensions
{
    public static IServiceCollection AddSettingsManagementDbContextStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> setupAction
    )
    {
        services.AddPooledDbContextFactory<SettingsDbContext>(setupAction);
        services.AddSettingsManagementDbContextStorage<SettingsDbContext>();

        return services;
    }

    public static IServiceCollection AddSettingsManagementDbContextStorage(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> setupAction
    )
    {
        services.AddPooledDbContextFactory<SettingsDbContext>(setupAction);
        services.AddSettingsManagementDbContextStorage<SettingsDbContext>();

        return services;
    }

    public static IServiceCollection AddSettingsManagementDbContextStorage<TContext>(this IServiceCollection services)
        where TContext : DbContext, ISettingsDbContext
    {
        services.AddSingleton<ISettingValueRecordRepository, EfSettingValueRecordRepository<TContext>>();
        services.AddSingleton<ISettingDefinitionRecordRepository, EfSettingDefinitionRecordRepository<TContext>>();

        return services;
    }
}
