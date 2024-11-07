// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class UserPermissionValueProvider(
    IPermissionGrantRepository permissionGrantRepository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant
) : StorePermissionValueProvider(permissionGrantRepository, guidGenerator, currentTenant)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;
}
