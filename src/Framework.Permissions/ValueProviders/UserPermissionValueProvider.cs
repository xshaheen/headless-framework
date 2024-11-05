// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Checks;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class UserPermissionValueProvider(IPermissionStore store) : StorePermissionValueProvider(store)
{
    public const string ProviderName = "User";

    public override string Name => ProviderName;

    public override async Task<PermissionGrantResult> GetResultAsync(PermissionValueCheckContext context)
    {
        var userId = context.Principal?.GetUserId();

        return userId is null ? PermissionGrantResult.Undefined
            : await PermissionStore.IsGrantedAsync(context.Permission.Name, Name, userId)
                ? PermissionGrantResult.Granted
            : PermissionGrantResult.Undefined;
    }

    public override async Task<MultiplePermissionGrantResult> GetResultAsync(PermissionValuesCheckContext context)
    {
        Argument.IsNotNullOrEmpty(context.Permissions);

        var permissionNames = context.Permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var userId = context.Principal?.GetUserId();

        return userId is null
            ? new MultiplePermissionGrantResult(permissionNames)
            : await PermissionStore.IsGrantedAsync(permissionNames, Name, userId);
    }
}
