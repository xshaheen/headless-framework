using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Entities;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

public interface IPermissionManagementProvider
{
    string Name { get; }

    Task<PermissionValueProviderGrantInfo> CheckAsync(string name, string providerName, string providerKey);

    Task<MultiplePermissionValueProviderGrantInfo> CheckAsync(string[] names, string providerName, string providerKey);

    Task SetAsync(string name, string providerKey, bool isGranted);
}

public abstract class PermissionManagementProvider(
    IPermissionGrantRepository repository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant
) : IPermissionManagementProvider
{
    public abstract string Name { get; }

    public virtual async Task<PermissionValueProviderGrantInfo> CheckAsync(
        string name,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionValueProviderGrantInfo = await CheckAsync([name], providerName, providerKey);

        return multiplePermissionValueProviderGrantInfo.Result.First().Value;
    }

    public virtual async Task<MultiplePermissionValueProviderGrantInfo> CheckAsync(
        string[] names,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionValueProviderGrantInfo = new MultiplePermissionValueProviderGrantInfo(names);

        if (!string.Equals(providerName, Name, StringComparison.Ordinal))
        {
            return multiplePermissionValueProviderGrantInfo;
        }

        var permissionGrants = await repository.GetListAsync(names, providerName, providerKey);

        foreach (var permissionName in names)
        {
            var isGrant = permissionGrants.Exists(x => string.Equals(x.Name, permissionName, StringComparison.Ordinal));

            multiplePermissionValueProviderGrantInfo.Result[permissionName] = new(isGrant, providerKey);
        }

        return multiplePermissionValueProviderGrantInfo;
    }

    public virtual Task SetAsync(string name, string providerKey, bool isGranted)
    {
        return isGranted ? GrantAsync(name, providerKey) : RevokeAsync(name, providerKey);
    }

    protected virtual async Task GrantAsync(string name, string providerKey)
    {
        var permissionGrant = await repository.FindAsync(name, Name, providerKey);
        if (permissionGrant != null)
        {
            return;
        }

        await repository.InsertAsync(
            new PermissionGrantRecord(guidGenerator.Create(), name, Name, providerKey, currentTenant.Id)
        );
    }

    protected virtual async Task RevokeAsync(string name, string providerKey)
    {
        var permissionGrant = await repository.FindAsync(name, Name, providerKey);

        if (permissionGrant is null)
        {
            return;
        }

        await repository.DeleteAsync(permissionGrant);
    }
}

public class UserPermissionManagementProvider(
    IPermissionGrantRepository permissionGrantRepository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant
) : PermissionManagementProvider(permissionGrantRepository, guidGenerator, currentTenant)
{
    public override string Name => UserPermissionValueProvider.ProviderName;
}

public interface IUserRoleFinder
{
    Task<string[]> GetRoleNamesAsync(Guid userId);
}

public class RolePermissionManagementProvider : PermissionManagementProvider
{
    public override string Name => RolePermissionValueProvider.ProviderName;

    protected IUserRoleFinder UserRoleFinder { get; }

    public RolePermissionManagementProvider(
        IPermissionGrantRepository permissionGrantRepository,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        IUserRoleFinder userRoleFinder
    )
        : base(permissionGrantRepository, guidGenerator, currentTenant)
    {
        UserRoleFinder = userRoleFinder;
    }

    public override async Task<PermissionValueProviderGrantInfo> CheckAsync(
        string name,
        string providerName,
        string providerKey
    )
    {
        var multipleGrantInfo = await CheckAsync(new[] { name }, providerName, providerKey);

        return multipleGrantInfo.Result.Values.First();
    }

    public override async Task<MultiplePermissionValueProviderGrantInfo> CheckAsync(
        string[] names,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionValueProviderGrantInfo = new MultiplePermissionValueProviderGrantInfo(names);
        var permissionGrants = new List<PermissionGrantRecord>();

        if (providerName == Name)
        {
            permissionGrants.AddRange(await PermissionGrantRepository.GetListAsync(names, providerName, providerKey));
        }

        if (providerName == UserPermissionValueProvider.ProviderName)
        {
            var userId = Guid.Parse(providerKey);
            var roleNames = await UserRoleFinder.GetRoleNamesAsync(userId);

            foreach (var roleName in roleNames)
            {
                permissionGrants.AddRange(await PermissionGrantRepository.GetListAsync(names, Name, roleName));
            }
        }

        permissionGrants = permissionGrants.Distinct().ToList();
        if (!permissionGrants.Any())
        {
            return multiplePermissionValueProviderGrantInfo;
        }

        foreach (var permissionName in names)
        {
            var permissionGrant = permissionGrants.FirstOrDefault(x => x.Name == permissionName);
            if (permissionGrant != null)
            {
                multiplePermissionValueProviderGrantInfo.Result[permissionName] = new PermissionValueProviderGrantInfo(
                    true,
                    permissionGrant.ProviderKey
                );
            }
        }

        return multiplePermissionValueProviderGrantInfo;
    }
}
