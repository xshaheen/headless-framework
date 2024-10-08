// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Settings.Models;

namespace Framework.Settings.ValueProviders;

public sealed class TenantSettingValueProvider(ISettingValueStore settingValueStore, ICurrentTenant currentTenant)
    : ISettingValueProvider
{
    public const string ProviderName = "Tenant";

    public string Name => ProviderName;

    public Task<string?> GetOrDefaultAsync(SettingDefinition setting)
    {
        return settingValueStore.GetOrDefaultAsync(setting.Name, Name, currentTenant.Id?.ToString());
    }

    public Task<List<SettingValue>> GetAllAsync(SettingDefinition[] settings)
    {
        return settingValueStore.GetAllAsync(
            names: settings.Select(x => x.Name).ToArray(),
            providerName: Name,
            providerKey: currentTenant.Id?.ToString()
        );
    }
}
