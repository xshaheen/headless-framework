// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values scoped to the current tenant.</summary>
public sealed class TenantSettingValueProvider(ISettingValueStore store, ICurrentTenant tenant)
    : StoreSettingValueProvider(store)
{
    /// <summary>The canonical provider name registered in <see cref="SettingValueProviderNames"/>.</summary>
    public const string ProviderName = SettingValueProviderNames.Tenant;

    /// <inheritdoc/>
    public override string Name => ProviderName;

    /// <summary>Returns <paramref name="providerKey"/> when explicitly supplied, otherwise falls back to the current tenant identifier.</summary>
    /// <param name="providerKey">An explicit tenant key, or <see langword="null"/> to use the ambient tenant.</param>
    /// <returns>The resolved tenant key.</returns>
    protected override string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey ?? tenant.Id;
    }
}
