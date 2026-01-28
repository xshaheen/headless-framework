// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

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
