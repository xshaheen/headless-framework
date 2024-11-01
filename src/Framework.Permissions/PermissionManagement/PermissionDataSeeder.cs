using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Entities;

namespace Framework.Permissions.PermissionManagement;

public sealed class PermissionDataSeeder(
    IPermissionGrantRepository permissionGrantRepository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant
) : IPermissionDataSeeder
{
    public async Task SeedAsync(
        string providerName,
        string providerKey,
        IEnumerable<string> grantedPermissions,
        Guid? tenantId = null
    )
    {
        using (currentTenant.Change(tenantId))
        {
            var names = grantedPermissions.ToArray();

            var permissionGrants = await permissionGrantRepository.GetListAsync(names, providerName, providerKey);
            var existsPermissionGrants = permissionGrants.ConvertAll(x => x.Name);

            foreach (var permissionName in names.Except(existsPermissionGrants, StringComparer.Ordinal))
            {
                var grant = new PermissionGrant(
                    guidGenerator.Create(),
                    permissionName,
                    providerName,
                    providerKey,
                    tenantId
                );

                await permissionGrantRepository.InsertAsync(grant);
            }
        }
    }
}
