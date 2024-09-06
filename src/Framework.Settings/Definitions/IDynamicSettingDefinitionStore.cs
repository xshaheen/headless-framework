// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Settings.Definitions;

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
