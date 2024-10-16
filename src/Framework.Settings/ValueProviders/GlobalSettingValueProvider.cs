// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

/// <summary>Provides setting values from the global store no key is required.</summary>
public sealed class GlobalSettingValueProvider(ISettingValueStore store) : StoreSettingValueProvider(store)
{
    public const string ProviderName = "Global";

    public override string Name => ProviderName;

    protected override string? NormalizeProviderKey(string? providerKey) => null;
}
