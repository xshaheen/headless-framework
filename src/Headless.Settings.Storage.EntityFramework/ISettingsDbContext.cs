// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Microsoft.EntityFrameworkCore;

namespace Headless.Settings.Storage.EntityFramework;

public interface ISettingsDbContext
{
    DbSet<SettingValueRecord> SettingValues { get; }

    DbSet<SettingDefinitionRecord> SettingDefinitions { get; }
}
