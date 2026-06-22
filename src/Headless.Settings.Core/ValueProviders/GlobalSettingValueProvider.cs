// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values from the global store; no provider key is required or used.</summary>
public sealed class GlobalSettingValueProvider(ISettingValueStore store) : StoreSettingValueProvider(store)
{
    /// <summary>The canonical provider name registered in <see cref="SettingValueProviderNames"/>.</summary>
    public const string ProviderName = SettingValueProviderNames.Global;

    /// <inheritdoc/>
    public override string Name => ProviderName;

    /// <summary>Always returns <see langword="null"/> — global settings are not scoped to a key.</summary>
    /// <param name="providerKey">Ignored.</param>
    /// <returns><see langword="null"/>.</returns>
    protected override string? NormalizeProviderKey(string? providerKey) => null;
}
