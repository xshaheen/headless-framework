// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Settings;

[PublicAPI]
public static class SettingsModelBuilderExtensions
{
    public static ModelBuilder AddHeadlessSettings(this ModelBuilder modelBuilder, SettingsStorageOptions options)
    {
        Argument.IsNotNull(modelBuilder);
        Argument.IsNotNull(options);

        if (modelBuilder.Model.FindEntityType(typeof(SettingValueRecord)) is not null)
        {
            return modelBuilder;
        }

        modelBuilder.ApplyConfiguration(new SettingValueRecordConfiguration(options));
        modelBuilder.ApplyConfiguration(new SettingDefinitionRecordConfiguration(options));

        return modelBuilder;
    }
}
