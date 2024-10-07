// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;

namespace Framework.Settings;

/// <summary>
/// The setting value provider is used to get the value of a setting from a specific source (e.g. database, file, etc.).
/// </summary>
public interface ISettingValueProvider
{
    string Name { get; }

    Task<string?> GetOrDefaultAsync(SettingDefinition setting);

    Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings);
}
