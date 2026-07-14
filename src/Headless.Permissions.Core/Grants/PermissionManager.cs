// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Checks;
using Headless.Exceptions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Permissions.Repositories;
using Headless.Permissions.Resources;

namespace Headless.Permissions.Grants;

/// <summary>
/// Default <see cref="IPermissionManager"/> implementation. Delegates resolution to the registered
/// <see cref="GrantProviders.IPermissionGrantProvider"/> chain using AWS IAM-style rules: an explicit <c>Prohibited</c>
/// from any provider overrides all grants; the default is deny.
/// </summary>
public sealed class PermissionManager(
    IPermissionDefinitionManager definitionManager,
    IPermissionGrantProviderManager grantProviderManager,
    IPermissionGrantRepository repository,
    IPermissionErrorsDescriptor errorsDescriptor
) : IPermissionManager
{
    public async Task<GrantedPermissionResult> GetAsync(
        string permissionName,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        var permission = await definitionManager.FindAsync(permissionName, cancellationToken).ConfigureAwait(false);

        if (permission?.IsEnabled != true)
        {
            return new(permissionName, isGranted: false);
        }

        var result = await _CoreGetOrDefaultAsync([permission], currentUser, providerName, cancellationToken)
            .ConfigureAwait(false);

        return result[0];
    }

    public async Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
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
            var permission = await definitionManager.FindAsync(permissionName, cancellationToken).ConfigureAwait(false);

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

        var result = await _CoreGetOrDefaultAsync(existPermissions, currentUser, providerName, cancellationToken)
            .ConfigureAwait(false);

        result.AddRange(undefinedPermissions.Select(name => new GrantedPermissionResult(name, isGranted: false)));

        return result;
    }

    public async Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        var allDefinitions = await definitionManager.GetPermissionsAsync(cancellationToken).ConfigureAwait(false);
        var result = await _CoreGetOrDefaultAsync(allDefinitions, currentUser, providerName, cancellationToken)
            .ConfigureAwait(false);

        return result;
    }

    public async Task SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var permission =
            await definitionManager.FindAsync(permissionName, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(errorsDescriptor.PermissionIsNotDefined(permissionName));

        if (!permission.IsEnabled)
        {
            throw new ConflictException(errorsDescriptor.PermissionDisabled(permission.Name));
        }

        if (permission.Providers.Count != 0 && !permission.Providers.Contains(providerName, StringComparer.Ordinal))
        {
            throw new ConflictException(errorsDescriptor.PermissionProviderNotDefined(permission.Name, providerName));
        }

        var provider =
            grantProviderManager.ValueProviders.FirstOrDefault(m =>
                string.Equals(m.Name, providerName, StringComparison.Ordinal)
            ) ?? throw new ConflictException(errorsDescriptor.PermissionsProviderNotFound(providerName));

        await provider.SetAsync(permission, providerKey, isGranted, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        var allDefinitions = await definitionManager.GetPermissionsAsync(cancellationToken).ConfigureAwait(false);

        var definedPermissions = allDefinitions
            .Where(x => permissionNames.Contains(x.Name, StringComparer.Ordinal))
            .ToList();

        var undefinedPermissions = permissionNames
            .Except(definedPermissions.Select(x => x.Name), StringComparer.Ordinal)
            .ToList();

        if (undefinedPermissions.Count != 0)
        {
            // Maybe they removed from dynamic permission definition store
            throw new ConflictException(errorsDescriptor.SomePermissionsAreNotDefined(undefinedPermissions));
        }

        var disabledPermissions = definedPermissions.Where(x => !x.IsEnabled).Select(x => x.Name).ToList();

        if (disabledPermissions.Count != 0)
        {
            throw new ConflictException(errorsDescriptor.SomePermissionsAreDisabled(disabledPermissions));
        }

        // Check if all permissions are granted
        var notDefinedProviderPermissions = definedPermissions
            .Where(x => x.Providers.Count != 0 && !x.Providers.Contains(providerName, StringComparer.Ordinal))
            .Select(x => x.Name)
            .ToList();

        if (notDefinedProviderPermissions.Count != 0)
        {
            throw new ConflictException(
                errorsDescriptor.ProviderNotDefinedForSomePermissions(notDefinedProviderPermissions, providerName)
            );
        }

        var provider =
            grantProviderManager.ValueProviders.FirstOrDefault(m =>
                string.Equals(m.Name, providerName, StringComparison.Ordinal)
            ) ?? throw new ConflictException(errorsDescriptor.PermissionsProviderNotFound(providerName));

        await provider.SetAsync(definedPermissions, providerKey, isGranted, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    )
    {
        var permissionGrants = await repository
            .GetListAsync(providerName, providerKey, cancellationToken)
            .ConfigureAwait(false);

        await repository.DeleteManyAsync(permissionGrants, cancellationToken).ConfigureAwait(false);
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

        // Evaluate each matching provider exactly once - CheckAsync typically fans out to the distributed
        // grant cache (per role for the role provider), so the denial and grant passes below share these
        // results instead of doubling those round-trips per authorization check.
        var providerGrantsList = new List<(string ProviderName, MultiplePermissionGrantStatusResult Grants)>();

        foreach (var provider in grantProviderManager.ValueProviders)
        {
            if (providerName is not null && !string.Equals(provider.Name, providerName, StringComparison.Ordinal))
            {
                continue;
            }

            var providerGrants = await provider
                .CheckAsync(checkNeededPermissions, currentUser, cancellationToken)
                .ConfigureAwait(false);

            providerGrantsList.Add((provider.Name, providerGrants));
        }

        // First pass: check for explicit denials (Prohibited)
        // AWS IAM-style: explicit deny overrides all grants
        var explicitDenials = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, providerGrants) in providerGrantsList)
        {
            foreach (var (permissionName, providerResult) in providerGrants.Statuses)
            {
                if (providerResult.Status is PermissionGrantStatus.Prohibited)
                {
                    explicitDenials.Add(permissionName);
                }
            }
        }

        // Second pass: apply grants only if not explicitly denied. Index results by name once instead of a
        // linear First() scan per granted permission (quadratic for large permission batches).
        var resultsByName = result.ToDictionary(x => x.Name, StringComparer.Ordinal);

        foreach (var (grantProviderName, providerGrants) in providerGrantsList)
        {
            foreach (var (permissionName, providerResult) in providerGrants.Statuses)
            {
                if (providerResult.Status is not PermissionGrantStatus.Granted)
                {
                    continue;
                }

                // Explicit deny overrides grant
                if (explicitDenials.Contains(permissionName))
                {
                    continue;
                }

                var grant = resultsByName[permissionName];

                grant.IsGranted = true;
                grant.AddProvider(new GrantPermissionProvider(grantProviderName, providerResult.ProviderKeys));
            }
        }

        return result;
    }

    #endregion
}
