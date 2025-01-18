// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Definitions;
using Framework.Settings.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Settings;

[PublicAPI]
public static class AddSettingsManagementEntityFrameworkStorageExtensions
{
    public static IServiceCollection AddSettingsManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction
    )
    {
        services.AddSingleton<ISettingValueRecordRepository, EfSettingValueRecordRepository>();
        services.AddSingleton<ISettingDefinitionRecordRepository, EfSettingDefinitionRecordRepository>();
        services.AddPooledDbContextFactory<SettingsDbContext>(optionsAction);

        return services;
    }
}
