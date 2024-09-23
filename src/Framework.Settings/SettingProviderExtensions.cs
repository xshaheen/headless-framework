// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Settings;

public static class SettingProviderExtensions
{
    public static async Task<bool> IsTrueAsync(this ISettingProvider settingProvider, string name)
    {
        var value = await settingProvider.GetOrDefaultAsync(name);

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> IsFalseAsync(this ISettingProvider settingProvider, string name)
    {
        var value = await settingProvider.GetOrDefaultAsync(name);

        return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<T> GetAsync<T>(
        this ISettingProvider settingProvider,
        string name,
        T defaultValue = default
    )
        where T : struct
    {
        var value = await settingProvider.GetOrDefaultAsync(name);

        return value?.To<T>() ?? defaultValue;
    }
}
