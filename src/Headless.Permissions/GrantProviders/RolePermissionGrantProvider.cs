// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;

namespace Headless.Permissions;

/// <summary>
/// Grant provider that resolves permissions via the user's roles. For each role assigned to the current
/// user, it queries <see cref="IPermissionGrantStore"/> with <c>providerName = "Role"</c> and
/// <c>providerKey = role name</c>. Resolution stops early when all permissions have a definitive status.
/// A permission remains <c>Undefined</c> when the user has no roles.
/// </summary>
[PublicAPI]
public sealed class RolePermissionGrantProvider(IPermissionGrantStore grantStore)
    : StorePermissionGrantProvider(grantStore)
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

        var permissionNames = permissions.Select(static x => x.Name).Distinct(StringComparer.Ordinal).ToList();
        var roles = currentUser.Roles;

        if (roles.Count == 0)
        {
            return new MultiplePermissionGrantStatusResult(permissionNames, roles, PermissionGrantStatus.Undefined);
        }

        // Assume all are undefined by default
        var result = new MultiplePermissionGrantStatusResult(permissionNames, roles, PermissionGrantStatus.Undefined);

        // One store call carries every role's grants for every pending name; the shape this replaces made
        // one call per role. Roles are still evaluated in the user's role order below, so the first role
        // with a decisive status for a name wins, exactly as the per-role loop decided.
        var grantsByRole = await _grantStore
            .IsGrantedAsync(
                names: permissionNames,
                providerName: Name,
                providerKeys: roles,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        foreach (var role in roles)
        {
            if (!grantsByRole.TryGetValue(role, out var roleGrantStatusResults))
            {
                continue;
            }

            foreach (var (permissionName, grantStatus) in roleGrantStatusResults)
            {
                if (
                    grantStatus is PermissionGrantStatus.Undefined
                    || !result.Statuses.TryGetValue(permissionName, out var existStatus)
                    || existStatus.Status is not PermissionGrantStatus.Undefined
                )
                {
                    continue;
                }

                result[permissionName] = grantStatus switch
                {
                    PermissionGrantStatus.Granted => PermissionGrantResult.Granted([role]),
                    PermissionGrantStatus.Prohibited => PermissionGrantResult.Prohibited([role]),
                    _ => PermissionGrantResult.Undefined(roles),
                };
            }
        }

        return result;
    }
}
