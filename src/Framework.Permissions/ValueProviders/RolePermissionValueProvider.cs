// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.Checks;
using Framework.Permissions.Checkers;
using Framework.Permissions.Models;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class RolePermissionValueProvider(IPermissionStore store) : PermissionValueProvider(store)
{
    public const string ProviderName = "Role";

    public override string Name => ProviderName;

    public override async Task<PermissionGrantResult> GetResultAsync(PermissionValueCheckContext context)
    {
        var roles = context.Principal?.GetRoles();

        if (roles is null || roles.Count == 0)
        {
            return PermissionGrantResult.Undefined;
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (await PermissionStore.IsGrantedAsync(context.Permission.Name, Name, role))
            {
                return PermissionGrantResult.Granted;
            }
        }

        return PermissionGrantResult.Undefined;
    }

    public override async Task<MultiplePermissionGrantResult> GetResultAsync(PermissionValuesCheckContext context)
    {
        Argument.IsNotNullOrEmpty(context.Permissions);

        var permissionNames = context.Permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var result = new MultiplePermissionGrantResult(permissionNames);

        var roles = context.Principal?.GetRoles();

        if (roles == null || roles.Count == 0)
        {
            return result;
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            var multipleResult = await PermissionStore.IsGrantedAsync(permissionNames, Name, role);

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
