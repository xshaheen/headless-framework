// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

public sealed class TenantSettingValueProvider(ISettingValueStore store, ICurrentTenant tenant)
    : StoreSettingValueProvider(store)
{
    public const string ProviderName = SettingValueProviderNames.Tenant;

    public override string Name => ProviderName;

    protected override string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey ?? tenant.Id;
    }
}
