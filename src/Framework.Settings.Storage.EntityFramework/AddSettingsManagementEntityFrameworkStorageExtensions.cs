// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;
using Framework.Settings.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Settings.Storage.EntityFramework;

public static class AddSettingsManagementEntityFrameworkStorageExtensions
{
    public static IHostApplicationBuilder AddSettingsManagementEntityFrameworkStorage(
        this IHostApplicationBuilder builder,
        Action<DbContextOptionsBuilder>? optionsAction = null,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped
    )
    {
        builder.Services.AddScoped<ISettingValueRecordRepository, EfSettingValueRecordRepository>();
        builder.Services.AddScoped<ISettingDefinitionRecordRepository, EfSettingDefinitionRecordRepository>();
        builder.Services.AddDbContext<SettingsDbContext>(optionsAction, contextLifetime, optionsLifetime);

        return builder;
    }
}
