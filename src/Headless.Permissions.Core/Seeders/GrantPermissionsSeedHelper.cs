// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Entities;
using Headless.Permissions.GrantProviders;
using Headless.Permissions.Repositories;

namespace Headless.Permissions.Seeders;

/// <summary>
/// Seed-time helper for granting permissions during data initialization. Intended for use inside
/// data seeders or hosted startup services, not in the application request path.
/// </summary>
public interface IGrantPermissionsSeedHelper
{
    /// <summary>
    /// Grants every currently-defined permission that allows the <c>Role</c> provider to the given role,
    /// skipping any permission that already has a grant record (idempotent). Runs under
    /// <paramref name="tenantId"/> when provided; otherwise runs under the ambient tenant.
    /// </summary>
    /// <param name="roleName">Name of the role to receive the grants.</param>
    /// <param name="tenantId">Optional tenant to scope the grants; uses the ambient tenant when <see langword="null"/>.</param>
    ValueTask GrantAllPermissionsToRoleAsync(
        string roleName,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class GrantPermissionsSeedHelper(
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionGrantRepository permissionGrantRepository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant
) : IGrantPermissionsSeedHelper
{
    public async ValueTask GrantAllPermissionsToRoleAsync(
        string roleName,
        string? tenantId = null,
        CancellationToken cancellationToken = default
    )
    {
        using var _ = currentTenant.Change(tenantId);

        var allPermissionNames = await _GetAllPermissionNamesAsync(cancellationToken).ConfigureAwait(false);

        var existsPermissionGrants = await _GetExistsPermissionGrantsAsync(
                roleName,
                allPermissionNames,
                cancellationToken
            )
            .ConfigureAwait(false);

        var notExistPermissionGrants = allPermissionNames
            .Except(existsPermissionGrants, StringComparer.Ordinal)
            .Select(permissionName => new PermissionGrantRecord(
                id: guidGenerator.Create(),
                name: permissionName,
                providerName: RolePermissionGrantProvider.ProviderName,
                providerKey: roleName,
                isGranted: true,
                tenantId: currentTenant.Id
            ));

        await permissionGrantRepository
            .InsertManyAsync(notExistPermissionGrants, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<List<string>> _GetExistsPermissionGrantsAsync(
        string roleName,
        string[] allPermissionNames,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrants = await permissionGrantRepository
            .GetListAsync(allPermissionNames, RolePermissionGrantProvider.ProviderName, roleName, cancellationToken)
            .ConfigureAwait(false);

        var existsPermissionGrants = permissionGrants.ConvertAll(x => x.Name);

        return existsPermissionGrants;
    }

    private async Task<string[]> _GetAllPermissionNamesAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await permissionDefinitionManager
            .GetPermissionsAsync(cancellationToken)
            .ConfigureAwait(false);

        var permissionNames = permissions
            .Where(p =>
                p.Providers.Count == 0
                || p.Providers.Contains(RolePermissionGrantProvider.ProviderName, StringComparer.Ordinal)
            )
            .Select(p => p.Name)
            .ToArray();

        return permissionNames;
    }
}
