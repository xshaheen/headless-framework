// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values from the global store no key is required.</summary>
public sealed class GlobalSettingValueProvider(ISettingValueStore store) : StoreSettingValueProvider(store)
{
    public const string ProviderName = SettingValueProviderNames.Global;

    public override string Name => ProviderName;

    protected override string? NormalizeProviderKey(string? providerKey) => null;
}
