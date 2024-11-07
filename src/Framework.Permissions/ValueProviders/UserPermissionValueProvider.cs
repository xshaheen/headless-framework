// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class UserPermissionValueProvider(
    IPermissionGrantStore permissionGrantStore,
    ICurrentTenant currentTenant
) : StorePermissionValueProvider(permissionGrantStore, currentTenant)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;
}
