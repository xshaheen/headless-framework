// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;

namespace Headless.Permissions.Testing;

/// <summary>Always allows for any permission.</summary>
public sealed class AlwaysAllowPermissionManager(IPermissionDefinitionManager definitionManager) : IPermissionManager
{
    public Task<GrantedPermissionResult> GetAsync(
        string permissionName,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new GrantedPermissionResult(permissionName, isGranted: true));
    }

    public async Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await definitionManager.GetPermissionsAsync(cancellationToken);

        return definitions.Select(x => new GrantedPermissionResult(x.Name, isGranted: true)).ToList();
    }

    public Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        IReadOnlyCollection<string> permissionNames,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<IReadOnlyList<GrantedPermissionResult>>(
            permissionNames.Select(x => new GrantedPermissionResult(x, isGranted: true)).ToList()
        );
    }

    public Task SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }

    public Task SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
