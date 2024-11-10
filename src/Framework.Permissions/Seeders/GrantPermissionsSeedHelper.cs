using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Definitions;
using Framework.Permissions.Entities;
using Framework.Permissions.GrantProviders;
using Framework.Permissions.Grants;

namespace Framework.Permissions.Seeders;

public interface IGrantPermissionsSeedHelper
{
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

        var allPermissionNames = await _GetAllPermissionNamesAsync(cancellationToken);

        var existsPermissionGrants = await _GetExistsPermissionGrantsAsync(
            roleName,
            allPermissionNames,
            cancellationToken
        );

        var notExistPermissionGrants = allPermissionNames
            .Except(existsPermissionGrants, StringComparer.Ordinal)
            .Select(permissionName => new PermissionGrantRecord(
                id: guidGenerator.Create(),
                name: permissionName,
                providerName: RolePermissionGrantProvider.ProviderName,
                providerKey: roleName,
                tenantId: currentTenant.Id
            ));

        await permissionGrantRepository.InsertManyAsync(notExistPermissionGrants, cancellationToken);
    }

    private async Task<List<string>> _GetExistsPermissionGrantsAsync(
        string roleName,
        string[] allPermissionNames,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrants = await permissionGrantRepository.GetListAsync(
            allPermissionNames,
            RolePermissionGrantProvider.ProviderName,
            roleName,
            cancellationToken
        );

        var existsPermissionGrants = permissionGrants.ConvertAll(x => x.Name);

        return existsPermissionGrants;
    }

    private async Task<string[]> _GetAllPermissionNamesAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await permissionDefinitionManager.GetPermissionsAsync(cancellationToken);

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
