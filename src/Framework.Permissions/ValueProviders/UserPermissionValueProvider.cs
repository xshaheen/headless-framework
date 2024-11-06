// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Checks;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class UserPermissionValueProvider(IPermissionValueStore store) : StorePermissionValueProvider(store)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;

    public override async Task<PermissionGrantResult> GetResultAsync(
        PermissionDefinition permission,
        ClaimsPrincipal? principal
    )
    {
        var userId = principal?.GetUserId();

        return userId is null ? PermissionGrantResult.Undefined
            : await PermissionValueStore.IsGrantedAsync(permission.Name, Name, userId) ? PermissionGrantResult.Granted
            : PermissionGrantResult.Undefined;
    }

    public override async Task<MultiplePermissionGrantResult> GetResultAsync(
        List<PermissionDefinition> permissions,
        ClaimsPrincipal? principal
    )
    {
        Argument.IsNotNullOrEmpty(permissions);

        var permissionNames = permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var userId = principal?.GetUserId();

        return userId is null
            ? new MultiplePermissionGrantResult(permissionNames)
            : await PermissionValueStore.IsGrantedAsync(permissionNames, Name, userId);
    }
}
