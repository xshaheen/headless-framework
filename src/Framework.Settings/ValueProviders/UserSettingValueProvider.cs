// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Exceptions;
using Framework.Settings.Helpers;
using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

/// <summary>Current user setting value provider.</summary>
public sealed class UserSettingValueProvider(
    ISettingValueStore store,
    ICurrentUser user,
    ISettingsErrorsProvider errorsProvider
) : StoreSettingValueProvider(store)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;

    protected override async ValueTask<string?> NormalizeProviderKey(string? providerKey)
    {
        return providerKey
            ?? user.UserId?.ToString()
            ?? throw new ConflictException(await errorsProvider.CurrentUserNotAvailable());
    }
}
