// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;

namespace Headless.Permissions.GrantProviders;

/// <summary>
/// Grant provider that resolves permissions via the user's roles. For each role assigned to the current
/// user, it queries <see cref="IPermissionGrantStore"/> with <c>providerName = "Role"</c> and
/// <c>providerKey = role name</c>. Resolution stops early when all permissions have a definitive status.
/// A permission remains <c>Undefined</c> when the user has no roles.
/// </summary>
[PublicAPI]
public sealed class RolePermissionGrantProvider(IPermissionGrantStore grantStore, ICurrentTenant currentTenant)
    : StorePermissionGrantProvider(grantStore, currentTenant)
{
    private readonly IPermissionGrantStore _grantStore = grantStore;

    /// <summary>The fixed provider name (<c>"Role"</c>) used as the <c>providerName</c> key in the grant store.</summary>
    public const string ProviderName = PermissionGrantProviderNames.Role;

    /// <inheritdoc/>
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
            var roleGrantStatusResults = await _grantStore
                .IsGrantedAsync(
                    names: permissionNames,
                    providerName: Name,
                    providerKey: role,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            var foundedStatuses = roleGrantStatusResults.Where(newStatus =>
                newStatus.Value is not PermissionGrantStatus.Undefined
                && result.Statuses.TryGetValue(newStatus.Key, out var existStatus)
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
