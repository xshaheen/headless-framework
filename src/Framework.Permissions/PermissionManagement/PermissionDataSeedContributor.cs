using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Definitions;
using Framework.Permissions.ValueProviders;

namespace Framework.Permissions.PermissionManagement;

public sealed class PermissionDataSeedContributor(
    IPermissionDefinitionManager permissionDefinitionManager,
    IPermissionDataSeeder permissionDataSeeder,
    ICurrentTenant currentTenant
) : IDataSeedContributor
{
    private IPermissionDataSeeder PermissionDataSeeder = permissionDataSeeder;

    public virtual async Task SeedAsync(DataSeedContext context)
    {
        var multiTenancySide = currentTenant.GetMultiTenancySide();

        var permissionNames = (await permissionDefinitionManager.GetAllPermissionsAsync())
            .Where(p => p.MultiTenancySide.HasFlag(multiTenancySide))
            .Where(p => !p.Providers.Any() || p.Providers.Contains(RolePermissionValueProvider.ProviderName))
            .Select(p => p.Name)
            .ToArray();

        await PermissionDataSeeder.SeedAsync(
            RolePermissionValueProvider.ProviderName,
            "admin",
            permissionNames,
            context?.TenantId
        );
    }
}
