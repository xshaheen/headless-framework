// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Context;

namespace Headless.Permissions;

/// <summary>
/// Grant provider that resolves permissions via the user's roles. For each role assigned to the current
/// user, it queries <see cref="IPermissionGrantStore"/> with <c>providerName = "Role"</c> and
/// <c>providerKey = role name</c>. Roles carry no order: a permission any role prohibits is
/// <c>Prohibited</c>, otherwise one any role grants is <c>Granted</c>. A permission remains <c>Undefined</c>
/// when no role has a record for it or the user has no roles.
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

        // One store call carries every role's grants for every pending name.
        var grantsByRole = await _grantStore
            .IsGrantedAsync(
                names: permissionNames,
                providerName: Name,
                providerKeys: roles,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        // The user's roles are a set with no order, so "the first role decides" would let hash order pick the
        // winner when roles conflict. Roles combine the way providers do: a prohibition from any role denies,
        // otherwise a grant from any role grants. Every deciding role is reported, in ordinal order.
        Dictionary<string, List<string>>? granting = null;
        Dictionary<string, List<string>>? prohibiting = null;

        foreach (var role in roles)
        {
            if (!grantsByRole.TryGetValue(role, out var roleGrantStatusResults))
            {
                continue;
            }

            foreach (var (permissionName, grantStatus) in roleGrantStatusResults)
            {
                if (!result.Statuses.ContainsKey(permissionName))
                {
                    continue;
                }

                var target = grantStatus switch
                {
                    PermissionGrantStatus.Granted => granting ??= new(StringComparer.Ordinal),
                    PermissionGrantStatus.Prohibited => prohibiting ??= new(StringComparer.Ordinal),
                    _ => null,
                };

                if (target is null)
                {
                    continue;
                }

                if (!target.TryGetValue(permissionName, out var deciders))
                {
                    target[permissionName] = deciders = [];
                }

                deciders.Add(role);
            }
        }

        if (granting is not null)
        {
            foreach (var (permissionName, deciders) in granting)
            {
                deciders.Sort(StringComparer.Ordinal);
                result[permissionName] = PermissionGrantResult.Granted(deciders);
            }
        }

        if (prohibiting is not null)
        {
            // Applied after grants so a prohibition replaces any grant for the same name.
            foreach (var (permissionName, deciders) in prohibiting)
            {
                deciders.Sort(StringComparer.Ordinal);
                result[permissionName] = PermissionGrantResult.Prohibited(deciders);
            }
        }

        return result;
    }
}
