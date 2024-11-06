// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Checks;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class RolePermissionValueProvider(IPermissionValueStore store) : StorePermissionValueProvider(store)
{
    public const string ProviderName = "Role";

    public override string Name => ProviderName;

    public override async Task<PermissionGrantResult> GetResultAsync(
        PermissionDefinition permission,
        ClaimsPrincipal? principal
    )
    {
        var roles = principal?.GetRoles();

        if (roles is null || roles.Count == 0)
        {
            return PermissionGrantResult.Undefined;
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (await PermissionValueStore.IsGrantedAsync(permission.Name, Name, role))
            {
                return PermissionGrantResult.Granted;
            }
        }

        return PermissionGrantResult.Undefined;
    }

    public override async Task<MultiplePermissionGrantResult> GetResultAsync(
        List<PermissionDefinition> permissions,
        ClaimsPrincipal? principal
    )
    {
        Argument.IsNotNullOrEmpty(permissions);

        var permissionNames = permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var result = new MultiplePermissionGrantResult(permissionNames);

        var roles = principal?.GetRoles();

        if (roles == null || roles.Count == 0)
        {
            return result;
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            var multipleResult = await PermissionValueStore.IsGrantedAsync(permissionNames, Name, role);

            foreach (
                var grantResult in multipleResult.Result.Where(grantResult =>
                    result.Result.ContainsKey(grantResult.Key)
                    && result.Result[grantResult.Key] == PermissionGrantResult.Undefined
                    && grantResult.Value != PermissionGrantResult.Undefined
                )
            )
            {
                result.Result[grantResult.Key] = grantResult.Value;
                permissionNames.RemoveAll(x => string.Equals(x, grantResult.Key, StringComparison.Ordinal));
            }

            if (result.AllGranted || result.AllProhibited)
            {
                break;
            }

            if (permissionNames.IsNullOrEmpty())
            {
                break;
            }
        }

        return result;
    }
}
