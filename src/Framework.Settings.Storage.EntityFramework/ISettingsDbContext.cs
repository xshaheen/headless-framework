// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Framework.Settings;

public interface ISettingsDbContext
{
    DbSet<SettingValueRecord> SettingValues { get; }

    DbSet<SettingDefinitionRecord> SettingDefinitions { get; }
}
