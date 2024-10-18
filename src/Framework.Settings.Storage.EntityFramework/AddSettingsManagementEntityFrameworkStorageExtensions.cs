// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;
using Framework.Settings.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Settings.Storage.EntityFramework;

public static class AddSettingsManagementEntityFrameworkStorageExtensions
{
    public static IServiceCollection AddSettingsManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction
    )
    {
        services.AddSingleton<ISettingValueRecordRepository, EfSettingValueRecordRepository>();
        services.AddSingleton<ISettingDefinitionRecordRepository, EfSettingDefinitionRecordRepository>();
        services.AddDbContextPool<SettingsDbContext>(optionsAction);

        return services;
    }
}
