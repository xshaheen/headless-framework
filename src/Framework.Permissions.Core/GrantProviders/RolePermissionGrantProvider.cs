// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Checks;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;

namespace Framework.Permissions.GrantProviders;

[PublicAPI]
public sealed class RolePermissionGrantProvider(IPermissionGrantStore grantStore, ICurrentTenant currentTenant)
    : StorePermissionGrantProvider(grantStore, currentTenant)
{
    private readonly IPermissionGrantStore _grantStore = grantStore;

    public const string ProviderName = PermissionGrantProviderNames.Role;

    public override string Name => ProviderName;

    public override async Task<MultiplePermissionGrantStatusResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(permissions);

        var permissionNames = permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToList();
        var roles = currentUser.Roles;

        if (roles.Count == 0)
        {
            return new MultiplePermissionGrantStatusResult(permissionNames, roles, PermissionGrantStatus.Undefined);
        }

        // Assume all are undefined by default
        var result = new MultiplePermissionGrantStatusResult(permissionNames, roles, PermissionGrantStatus.Undefined);

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

            if (result.AllGranted || result.AllProhibited || permissionNames.Count == 0)
            {
                break;
            }
        }

        return result;
    }
}
