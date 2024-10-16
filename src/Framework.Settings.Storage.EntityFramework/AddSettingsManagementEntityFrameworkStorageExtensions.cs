// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;
using Framework.Settings.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Settings.Storage.EntityFramework;

public static class AddSettingsManagementEntityFrameworkStorageExtensions
{
    public static IServiceCollection AddSettingsManagementEntityFrameworkStorage(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? optionsAction = null,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
    )
    {
        services.AddScoped<ISettingValueRecordRepository, EfSettingValueRecordRepository>();
        services.AddScoped<ISettingDefinitionRecordRepository, EfSettingDefinitionRecordRepository>();
        services.AddDbContext<SettingsDbContext>(optionsAction, contextLifetime, optionsLifetime);

        return services;
    }
}
