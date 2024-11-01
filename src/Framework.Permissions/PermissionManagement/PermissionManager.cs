using Framework.Caching;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Permissions.Definitions;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Permissions.PermissionManagement;

public sealed class PermissionManager : IPermissionManager
{
    private readonly IPermissionGrantRepository _permissionGrantRepository;
    private readonly IPermissionDefinitionManager _permissionDefinitionManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICache<PermissionGrantCacheItem> _cache;
    private readonly PermissionManagementOptions _options;

    private readonly Lazy<List<IPermissionManagementProvider>> _lazyProviders;

    public PermissionManager(
        IPermissionDefinitionManager permissionDefinitionManager,
        IPermissionGrantRepository permissionGrantRepository,
        IServiceProvider serviceProvider,
        IGuidGenerator guidGenerator,
        ICurrentTenant currentTenant,
        ICache<PermissionGrantCacheItem> cache,
        IOptions<PermissionManagementOptions> options
    )
    {
        _guidGenerator = guidGenerator;
        _currentTenant = currentTenant;
        _cache = cache;
        _simpleStateCheckerManager = simpleStateCheckerManager;
        _permissionGrantRepository = permissionGrantRepository;
        _permissionDefinitionManager = permissionDefinitionManager;
        _options = options.Value;

        _lazyProviders = new Lazy<List<IPermissionManagementProvider>>(
            () =>
                _options
                    .ManagementProviders.Select(c =>
                        serviceProvider.GetRequiredService(c) as IPermissionManagementProvider
                    )
                    .ToList(),
            true
        );
    }

    public async Task<PermissionWithGrantedProviders> GetAsync(
        string permissionName,
        string providerName,
        string providerKey
    )
    {
        var permission = await _permissionDefinitionManager.GetOrNullAsync(permissionName);
        if (permission == null)
        {
            return new PermissionWithGrantedProviders(permissionName, false);
        }

        return await _GetInternalAsync((PermissionDefinition)permission, providerName, providerKey);
    }

    public async Task<MultiplePermissionWithGrantedProviders> GetAsync(
        string[] permissionNames,
        string providerName,
        string providerKey
    )
    {
        var permissions = new List<PermissionDefinition>();
        var undefinedPermissions = new List<string>();

        foreach (var permissionName in permissionNames)
        {
            var permission = await _permissionDefinitionManager.GetOrNullAsync(permissionName);
            if (permission != null)
            {
                permissions.Add(permission);
            }
            else
            {
                undefinedPermissions.Add(permissionName);
            }
        }

        if (!permissions.Any())
        {
            return new MultiplePermissionWithGrantedProviders(undefinedPermissions.ToArray());
        }

        var result = await _GetInternalAsync(permissions.ToArray(), providerName, providerKey);

        foreach (var undefinedPermission in undefinedPermissions)
        {
            result.Result.Add(new PermissionWithGrantedProviders(undefinedPermission, false));
        }

        return result;
    }

    public async Task<List<PermissionWithGrantedProviders>> GetAllAsync(string providerName, string providerKey)
    {
        var permissionDefinitions = (await _permissionDefinitionManager.GetAllPermissionsAsync()).ToArray();

        var multiplePermissionWithGrantedProviders = await _GetInternalAsync(
            permissionDefinitions,
            providerName,
            providerKey
        );

        return multiplePermissionWithGrantedProviders.Result;
    }

    public async Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted)
    {
        var permission = await _permissionDefinitionManager.GetOrNullAsync(permissionName);
        if (permission == null)
        {
            /* Silently ignore undefined permissions,
               maybe they were removed from dynamic permission definition store */
            return;
        }

        if (!permission.IsEnabled || !await _simpleStateCheckerManager.IsEnabledAsync(permission))
        {
            //TODO: BusinessException
            throw new ApplicationException($"The permission named '{permission.Name}' is disabled!");
        }

        if (permission.Providers.Any() && !permission.Providers.Contains(providerName))
        {
            //TODO: BusinessException
            throw new ApplicationException(
                $"The permission named '{permission.Name}' has not compatible with the provider named '{providerName}'"
            );
        }

        if (!permission.MultiTenancySide.HasFlag(_currentTenant.GetMultiTenancySide()))
        {
            //TODO: BusinessException
            throw new ApplicationException(
                $"The permission named '{permission.Name}' has multitenancy side '{permission.MultiTenancySide}' which is not compatible with the current multitenancy side '{_currentTenant.GetMultiTenancySide()}'"
            );
        }

        var currentGrantInfo = await _GetInternalAsync((PermissionDefinition)permission, providerName, providerKey);
        if (currentGrantInfo.IsGranted == isGranted)
        {
            return;
        }

        var provider = _lazyProviders.Value.FirstOrDefault(m => m.Name == providerName);
        if (provider == null)
        {
            //TODO: BusinessException
            throw new AbpException("Unknown permission management provider: " + providerName);
        }

        await provider.SetAsync(permissionName, providerKey, isGranted);
    }

    public async Task<PermissionGrant> UpdateProviderKeyAsync(PermissionGrant permissionGrant, string providerKey)
    {
        using (_currentTenant.Change(permissionGrant.TenantId))
        {
            //Invalidating the cache for the old key
            await _cache.RemoveAsync(
                PermissionGrantCacheItem.CalculateCacheKey(
                    permissionGrant.Name,
                    permissionGrant.ProviderName,
                    permissionGrant.ProviderKey
                )
            );
        }

        permissionGrant.ProviderKey = providerKey;
        return await _permissionGrantRepository.UpdateAsync(permissionGrant);
    }

    public async Task DeleteAsync(string providerName, string providerKey)
    {
        var permissionGrants = await _permissionGrantRepository.GetListAsync(providerName, providerKey);
        foreach (var permissionGrant in permissionGrants)
        {
            await _permissionGrantRepository.DeleteAsync(permissionGrant);
        }
    }

    private async Task<PermissionWithGrantedProviders> _GetInternalAsync(
        PermissionDefinition permission,
        string providerName,
        string providerKey
    )
    {
        var multiplePermissionWithGrantedProviders = await _GetInternalAsync([permission], providerName, providerKey);

        return multiplePermissionWithGrantedProviders.Result[0];
    }

    private async Task<MultiplePermissionWithGrantedProviders> _GetInternalAsync(
        PermissionDefinition[] permissions,
        string providerName,
        string providerKey
    )
    {
        var permissionNames = permissions.Select(x => x.Name).ToArray();
        var multiplePermissionWithGrantedProviders = new MultiplePermissionWithGrantedProviders(permissionNames);

        var neededCheckPermissions = new List<PermissionDefinition>();

        foreach (
            var permission in permissions
                .Where(x => x.IsEnabled)
                .Where(x => x.MultiTenancySide.HasFlag(_currentTenant.GetMultiTenancySide()))
                .Where(x => !x.Providers.Any() || x.Providers.Contains(providerName))
        )
        {
            if (await _simpleStateCheckerManager.IsEnabledAsync(permission))
            {
                neededCheckPermissions.Add(permission);
            }
        }

        if (neededCheckPermissions.Count == 0)
        {
            return multiplePermissionWithGrantedProviders;
        }

        foreach (var provider in _lazyProviders.Value)
        {
            permissionNames = neededCheckPermissions.Select(x => x.Name).ToArray();
            var multiplePermissionValueProviderGrantInfo = await provider.CheckAsync(
                permissionNames,
                providerName,
                providerKey
            );

            foreach (var providerResultDict in multiplePermissionValueProviderGrantInfo.Result)
            {
                if (providerResultDict.Value.IsGranted)
                {
                    var permissionWithGrantedProvider = multiplePermissionWithGrantedProviders.Result.First(x =>
                        string.Equals(x.Name, providerResultDict.Key, StringComparison.Ordinal)
                    );

                    permissionWithGrantedProvider.IsGranted = true;

                    permissionWithGrantedProvider.Providers.Add(
                        new PermissionValueProviderInfo(provider.Name, providerResultDict.Value.ProviderKey)
                    );
                }
            }
        }

        return multiplePermissionWithGrantedProviders;
    }
}
