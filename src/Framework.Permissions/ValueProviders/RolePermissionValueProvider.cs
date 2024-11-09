// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Permissions.Results;

namespace Framework.Permissions.ValueProviders;

[PublicAPI]
public sealed class RolePermissionValueProvider(IPermissionGrantStore grantStore, ICurrentTenant currentTenant)
    : StorePermissionValueProvider(grantStore, currentTenant)
{
    private readonly IPermissionGrantStore _grantStore = grantStore;

    public const string ProviderName = "Role";

    public override string Name => ProviderName;

    public override async Task<MultiplePermissionGrantResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(permissions);

        var permissionNames = permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var roles = currentUser.Roles;

        if (roles.Count == 0)
        {
            return new MultiplePermissionGrantResult(permissionNames, roles, PermissionGrantStatus.Undefined);
        }

        // Assume all are undefined by default
        var result = new MultiplePermissionGrantResult(permissionNames, roles, PermissionGrantStatus.Undefined);

        foreach (var role in roles)
        {
            var roleGrantStatusResults = await _grantStore.IsGrantedAsync(
                names: permissionNames,
                providerName: Name,
                providerKey: role,
                cancellationToken: cancellationToken
            );

            var foundedStatuses = roleGrantStatusResults.Where(newStatus =>
                newStatus.Value is not PermissionGrantStatus.Undefined
                && result.TryGetValue(newStatus.Key, out var existStatus)
                && existStatus.Status is PermissionGrantStatus.Undefined
            );

            foreach (var (permissionName, grantStatus) in foundedStatuses)
            {
                result[permissionName] = grantStatus switch
                {
                    PermissionGrantStatus.Granted => PermissionGrantResult.Granted([role]),
                    PermissionGrantStatus.Prohibited => PermissionGrantResult.Prohibited([role]),
                    _ => PermissionGrantResult.Undefined(roles),
                };

                permissionNames.RemoveAll(name => string.Equals(name, permissionName, StringComparison.Ordinal));
            }

            if (result.AllGranted || result.AllProhibited || permissionNames.Length == 0)
            {
                break;
            }
        }

        return result;
    }
}
