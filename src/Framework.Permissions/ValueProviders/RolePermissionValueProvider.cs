// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;
using Framework.Permissions.Results;
using Framework.Permissions.Values;

namespace Framework.Permissions.ValueProviders;

public interface IUserRoleFinder
{
    Task<string[]> GetRoleNamesAsync(Guid userId);
}

public sealed class RolePermissionManagementProvider(
    IPermissionGrantRepository permissionGrantRepository,
    IGuidGenerator guidGenerator,
    ICurrentTenant currentTenant,
    IUserRoleFinder userRoleFinder
) : StorePermissionValueProvider(permissionGrantRepository, guidGenerator, currentTenant)
{
    private readonly IPermissionGrantRepository _permissionGrantRepository = permissionGrantRepository;

    public override string Name => RolePermissionValueProvider.ProviderName;

    public override async Task<PermissionGrantInfo> CheckAsync(string name, string providerName, string providerKey)
    {
        var multipleGrantInfo = await CheckAsync([name], providerName, providerKey);

        return multipleGrantInfo.Result.Values.First();
    }

    public override async Task<MultiplePermissionGrantResult> CheckAsync(
        string[] names,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionValueProviderGrantInfo = new MultiplePermissionGrantResult(names);
        var permissionGrants = new List<PermissionGrantRecord>();

        if (string.Equals(providerName, Name, StringComparison.Ordinal))
        {
            permissionGrants.AddRange(await _permissionGrantRepository.GetListAsync(names, providerName, providerKey));
        }

        if (string.Equals(providerName, UserPermissionValueProvider.ProviderName, StringComparison.Ordinal))
        {
            var userId = Guid.Parse(providerKey);
            var roleNames = await userRoleFinder.GetRoleNamesAsync(userId);

            foreach (var roleName in roleNames)
            {
                permissionGrants.AddRange(await _permissionGrantRepository.GetListAsync(names, Name, roleName));
            }
        }

        permissionGrants = permissionGrants.Distinct().ToList();

        if (permissionGrants.Count == 0)
        {
            return multiplePermissionValueProviderGrantInfo;
        }

        foreach (var permissionName in names)
        {
            var permissionGrant = permissionGrants.Find(x =>
                string.Equals(x.Name, permissionName, StringComparison.Ordinal)
            );

            if (permissionGrant != null)
            {
                multiplePermissionValueProviderGrantInfo.Result[permissionName] = new(
                    isGranted: true,
                    permissionGrant.ProviderKey
                );
            }
        }

        return multiplePermissionValueProviderGrantInfo;
    }
}

[PublicAPI]
public sealed class RolePermissionValueProvider(IPermissionGrantStore store) : StorePermissionValueProvider(store)
{
    public const string ProviderName = "Role";

    public override string Name => ProviderName;

    public async Task<PermissionGrantStatus> GetResultAsync(PermissionDefinition permission, ClaimsPrincipal? principal)
    {
        var roles = principal?.GetRoles();

        if (roles is null || roles.Count == 0)
        {
            return PermissionGrantStatus.Undefined;
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            if (await PermissionGrantStore.IsGrantedAsync(permission.Name, Name, role))
            {
                return PermissionGrantStatus.Granted;
            }
        }

        return PermissionGrantStatus.Undefined;
    }

    public async Task<MultiplePermissionGrantResult> GetResultAsync(
        List<PermissionDefinition> permissions,
        ClaimsPrincipal? principal
    )
    {
        Argument.IsNotNullOrEmpty(permissions);

        var permissionNames = permissions.Select(x => x.Name).Distinct(StringComparer.Ordinal).ToArray();
        var result = new MultiplePermissionGrantResult(permissionNames);

        var roles = principal?.GetRoles();

        if (roles == null || roles.Count == 0)
        {
            return result;
        }

        foreach (var role in roles.Distinct(StringComparer.Ordinal))
        {
            var multipleResult = await PermissionGrantStore.IsGrantedAsync(permissionNames, Name, role);

            foreach (
                var grantResult in multipleResult.Result.Where(grantResult =>
                    result.Result.ContainsKey(grantResult.Key)
                    && result.Result[grantResult.Key] == PermissionGrantStatus.Undefined
                    && grantResult.Value != PermissionGrantStatus.Undefined
                )
            )
            {
                result.Result[grantResult.Key] = grantResult.Value;
                permissionNames.RemoveAll(x => string.Equals(x, grantResult.Key, StringComparison.Ordinal));
            }

            if (result.AllGranted || result.AllProhibited)
            {
                break;
            }

            if (permissionNames.IsNullOrEmpty())
            {
                break;
            }
        }

        return result;
    }
}
