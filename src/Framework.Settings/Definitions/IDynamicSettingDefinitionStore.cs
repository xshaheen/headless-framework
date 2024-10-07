// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Models;

namespace Framework.Settings.Definitions;

/// <summary>Store for setting definitions that are defined dynamically from an external source like a database.</summary>
public interface IDynamicSettingDefinitionStore
{
    Task<IReadOnlyList<SettingDefinition>> GetAllAsync();

    Task<SettingDefinition?> GetOrDefaultAsync(string name);
}

public class NullDynamicSettingDefinitionStore : IDynamicSettingDefinitionStore
{
    private static readonly Task<SettingDefinition?> _CachedNullableSettingResult = Task.FromResult<SettingDefinition?>(
        null
    );

    private static readonly Task<IReadOnlyList<SettingDefinition>> _CachedSettingsResult = Task.FromResult(
        (IReadOnlyList<SettingDefinition>)[]
    );

    public Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        return _CachedSettingsResult;
    }

    public Task<SettingDefinition?> GetOrDefaultAsync(string name)
    {
        return _CachedNullableSettingResult;
    }
}
