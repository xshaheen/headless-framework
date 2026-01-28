// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Models;

namespace Headless.Permissions.Grants;

public interface IPermissionManager
{
    Task<GrantedPermissionResult> GetAsync(
        string permissionName,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        IReadOnlyCollection<string> permissionNames,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    Task SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
