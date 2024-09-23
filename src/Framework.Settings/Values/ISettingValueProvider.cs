// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Definitions;

namespace Framework.Settings.Values;

/// <summary>
/// The setting value provider is used to get the value of a setting from a specific source (e.g. database, file, etc.).
/// </summary>
public interface ISettingValueProvider
{
    string Name { get; }

    Task<string?> GetOrDefaultAsync(SettingDefinition setting);

    Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings);
}

/// <inheritdoc />
public abstract class SettingValueProvider(ISettingStore settingStore) : ISettingValueProvider
{
    public abstract string Name { get; }

    protected ISettingStore SettingStore { get; } = settingStore;

    public abstract Task<string?> GetOrDefaultAsync(SettingDefinition setting);

    public abstract Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings);
}
