// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Kernel.Checks;
using Framework.Kernel.Primitives;
using Framework.Permissions.Definitions;
using Framework.Permissions.Models;
using Framework.Permissions.Resources;
using Framework.Permissions.Results;

namespace Framework.Permissions.Grants;

public interface IPermissionManager
{
    Task<GrantedPermissionResult> GetAsync(
        string permissionName,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    Task<List<GrantedPermissionResult>> GetAllAsync(
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    Task<List<GrantedPermissionResult>> GetAllAsync(
        IReadOnlyCollection<string> permissionNames,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    Task<Result<ErrorDescriptor>> SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    Task<Result<ErrorDescriptor>> SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}

public sealed class PermissionManager(
    IPermissionDefinitionManager definitionManager,
    IPermissionGrantProviderManager grantProviderManager,
    IPermissionGrantRepository repository
) : IPermissionManager
{
    public async Task<GrantedPermissionResult> GetAsync(
        string permissionName,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        var permission = await definitionManager.GetOrDefaultAsync(permissionName, cancellationToken);

        if (permission is null || !permission.IsEnabled)
        {
            return new(permissionName, isGranted: false);
        }

        var result = await _CoreGetOrDefaultAsync([permission], currentUser, providerName, cancellationToken);

        return result[0];
    }

    public async Task<List<GrantedPermissionResult>> GetAllAsync(
        IReadOnlyCollection<string> permissionNames,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(permissionNames);
        Argument.IsNotNull(currentUser);

        if (permissionNames.Count == 0)
        {
            return [];
        }

        var existPermissions = new List<PermissionDefinition>();
        var undefinedPermissions = new List<string>();

        foreach (var permissionName in permissionNames)
        {
            var permission = await definitionManager.GetOrDefaultAsync(permissionName, cancellationToken);

            if (permission is not null)
            {
                existPermissions.Add(permission);
            }
            else
            {
                undefinedPermissions.Add(permissionName);
            }
        }

        if (existPermissions.Count == 0)
        {
            return undefinedPermissions.ConvertAll(name => new GrantedPermissionResult(name, isGranted: false));
        }

        var result = await _CoreGetOrDefaultAsync(existPermissions, currentUser, providerName, cancellationToken);

        result.AddRange(undefinedPermissions.Select(name => new GrantedPermissionResult(name, isGranted: false)));

        return result;
    }

    public async Task<List<GrantedPermissionResult>> GetAllAsync(
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        var allDefinitions = await definitionManager.GetPermissionsAsync(cancellationToken);
        var result = await _CoreGetOrDefaultAsync(allDefinitions, currentUser, providerName, cancellationToken);

        return result;
    }

    public async Task<Result<ErrorDescriptor>> SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var permission = await definitionManager.GetOrDefaultAsync(permissionName, cancellationToken);

        if (permission is null)
        {
            // Maybe they were removed from dynamic permission definition store
            return PermissionErrorDescriber.PermissionIsNotDefined(permissionName);
        }

        if (!permission.IsEnabled)
        {
            return PermissionErrorDescriber.PermissionDisabled(permission.Name);
        }

        if (permission.Providers.Count != 0 && !permission.Providers.Contains(providerName, StringComparer.Ordinal))
        {
            return PermissionErrorDescriber.PermissionProviderNotDefined(permission.Name, providerName);
        }

        var provider = grantProviderManager.ValueProviders.FirstOrDefault(m =>
            string.Equals(m.Name, providerName, StringComparison.Ordinal)
        );

        if (provider is null)
        {
            return PermissionErrorDescriber.PermissionsProviderNotFound(providerName);
        }

        await provider.SetAsync(permission, providerKey, isGranted, cancellationToken);

        return Result<ErrorDescriptor>.Success();
    }

    public async Task<Result<ErrorDescriptor>> SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var allDefinitions = await definitionManager.GetPermissionsAsync(cancellationToken);

        var definedPermissions = allDefinitions
            .Where(x => permissionNames.Contains(x.Name, StringComparer.Ordinal))
            .ToList();

        var undefinedPermissions = permissionNames
            .Except(definedPermissions.Select(x => x.Name), StringComparer.Ordinal)
            .ToList();

        if (undefinedPermissions.Count != 0)
        {
            // Maybe they removed from dynamic permission definition store
            return PermissionErrorDescriber.SomePermissionsAreNotDefined(undefinedPermissions);
        }

        var disabledPermissions = definedPermissions.Where(x => !x.IsEnabled).Select(x => x.Name).ToList();

        if (disabledPermissions.Count != 0)
        {
            return PermissionErrorDescriber.SomePermissionsAreDisabled(disabledPermissions);
        }

        // Check if all permissions are granted
        var notDefinedProviderPermissions = definedPermissions
            .Where(x => x.Providers.Count != 0 && !x.Providers.Contains(providerName, StringComparer.Ordinal))
            .Select(x => x.Name)
            .ToList();

        if (notDefinedProviderPermissions.Count != 0)
        {
            return PermissionErrorDescriber.ProviderNotDefinedForSomePermissions(
                notDefinedProviderPermissions,
                providerName
            );
        }

        var provider = grantProviderManager.ValueProviders.FirstOrDefault(m =>
            string.Equals(m.Name, providerName, StringComparison.Ordinal)
        );

        if (provider is null)
        {
            return PermissionErrorDescriber.PermissionsProviderNotFound(providerName);
        }

        await provider.SetAsync(definedPermissions, providerKey, isGranted, cancellationToken);

        return Result<ErrorDescriptor>.Success();
    }

    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrants = await repository.GetListAsync(providerName, providerKey, cancellationToken);

        await repository.DeleteManyAsync(permissionGrants, cancellationToken);
    }

    #region Helpers

    private async Task<List<GrantedPermissionResult>> _CoreGetOrDefaultAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        string? providerName,
        CancellationToken cancellationToken = default
    )
    {
        if (permissions.Count == 0)
        {
            return [];
        }

        // Assume all permissions are not granted
        var result = permissions.Select(x => new GrantedPermissionResult(x.Name, isGranted: false)).ToList();

        var checkNeededPermissions = permissions
            .Where(x =>
                x.IsEnabled
                && (
                    providerName is null
                    || x.Providers.Count == 0
                    || x.Providers.Contains(providerName, StringComparer.Ordinal)
                )
            )
            .ToList();

        if (checkNeededPermissions.Count == 0)
        {
            return result;
        }

        foreach (var provider in grantProviderManager.ValueProviders)
        {
            if (providerName is not null && !string.Equals(provider.Name, providerName, StringComparison.Ordinal))
            {
                continue;
            }

            var providerGrants = await provider.CheckAsync(checkNeededPermissions, currentUser, cancellationToken);

            foreach (var (permissionName, providerResult) in providerGrants)
            {
                if (providerResult.Status is not PermissionGrantStatus.Granted)
                {
                    continue;
                }

                var grant = result.First(x => string.Equals(x.Name, permissionName, StringComparison.Ordinal));

                grant.IsGranted = true;
                grant.Providers.Add(new GrantPermissionProvider(provider.Name, providerResult.ProviderKeys));
            }
        }

        return result;
    }

    #endregion
}
