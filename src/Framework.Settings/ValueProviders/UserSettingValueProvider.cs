// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Models;
using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

/// <summary>Current user setting value provider.</summary>
public sealed class UserSettingValueProvider(ISettingValueStore settingValueStore, ICurrentUser currentUser)
    : ISettingValueProvider
{
    public const string ProviderName = "User";

    public string Name => ProviderName;

    public async Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return currentUser.UserId is null
            ? null
            : await settingValueStore.GetOrDefaultAsync(setting.Name, Name, currentUser.UserId);
    }

    public async Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return currentUser.UserId is null
            ? settings.Select(x => new SettingValue(x.Name, value: null)).ToList()
            : await settingValueStore.GetAllAsync(settings.Select(x => x.Name).ToArray(), Name, currentUser.UserId);
    }
}
