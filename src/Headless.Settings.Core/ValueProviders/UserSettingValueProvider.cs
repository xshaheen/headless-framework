// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Settings.Values;

namespace Headless.Settings.ValueProviders;

/// <summary>Provides setting values scoped to the current user.</summary>
public sealed class UserSettingValueProvider(ISettingValueStore store, ICurrentUser user)
    : StoreSettingValueProvider(store)
{
    /// <summary>The canonical provider name registered in <see cref="SettingValueProviderNames"/>.</summary>
    public const string ProviderName = SettingValueProviderNames.User;

    /// <inheritdoc/>
    public override string Name => ProviderName;

    /// <summary>Returns <paramref name="providerKey"/> when explicitly supplied, otherwise falls back to the current user identifier.</summary>
    /// <param name="providerKey">An explicit user key, or <see langword="null"/> to use the ambient user.</param>
    /// <returns>The resolved user key, or <see langword="null"/> if no user is available.</returns>
    protected override string? NormalizeProviderKey(string? providerKey)
    {
        return providerKey ?? user.UserId?.ToString();
    }
}
