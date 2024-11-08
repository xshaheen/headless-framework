// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class RolePermissionValueProvider(
    IPermissionGrantStore permissionGrantStore,
    ICurrentTenant currentTenant
) : StorePermissionValueProvider(permissionGrantStore, currentTenant)
{
    private readonly IPermissionGrantStore _permissionGrantStore = permissionGrantStore;

    public const string ProviderName = "Role";

    public override string Name => ProviderName;

    public override async Task<MultiplePermissionGrantResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        string providerName,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(permissions);

        var permissionNames = permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var result = new MultiplePermissionGrantResult(permissionNames);

        if (!string.Equals(providerName, Name, StringComparison.Ordinal))
        {
            return new MultiplePermissionGrantResult(permissionNames);
        }

        var roles = currentUser.Roles;

        if (roles.Count == 0)
        {
            return result;
        }

        foreach (var role in roles)
        {
            var multipleResult = await _permissionGrantStore.IsGrantedAsync(
                names: permissionNames,
                providerName: Name,
                providerKey: role,
                cancellationToken: cancellationToken
            );

            var keyValuePairs = multipleResult.Result.Where(grantResult =>
                result.Result.ContainsKey(grantResult.Key)
                && result.Result[grantResult.Key].Status is PermissionGrantStatus.Undefined
                && grantResult.Value.Status is not PermissionGrantStatus.Undefined
            );

            foreach (var (key, grantResult) in keyValuePairs)
            {
                result.Result[key] = grantResult;
                permissionNames.RemoveAll(x => string.Equals(x, key, StringComparison.Ordinal));
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
