// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Exceptions;
using Framework.Settings.Helpers;
using Framework.Settings.Values;

namespace Framework.Settings.ValueProviders;

public sealed class TenantSettingValueProvider(
    ISettingValueStore store,
    ICurrentTenant tenant,
    ISettingsErrorsProvider errorsProvider
) : StoreSettingValueProvider(store)
{
    public const string ProviderName = "Tenant";

    public override string Name => ProviderName;

    protected override async ValueTask<string?> NormalizeProviderKey(string? providerKey)
    {
        return providerKey
            ?? tenant.Id
            ?? throw new ConflictException(await errorsProvider.CurrentTenantNotAvailable());
    }
}
