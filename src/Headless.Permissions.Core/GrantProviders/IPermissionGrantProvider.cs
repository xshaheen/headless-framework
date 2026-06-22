// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Models;

namespace Headless.Permissions.GrantProviders;

/// <summary>
/// Plug-in point for a single grant resolution strategy. The framework ships <see cref="UserPermissionGrantProvider"/>
/// and <see cref="RolePermissionGrantProvider"/> and calls each registered provider during resolution.
/// An explicit <c>Prohibited</c> result from any provider overrides grants from all others.
/// </summary>
public interface IPermissionGrantProvider
{
    /// <summary>
    /// Unique name that identifies this provider (e.g. <c>"User"</c> or <c>"Role"</c>).
    /// Used as the <c>providerName</c> argument to <see cref="Grants.IPermissionGrantStore"/> and
    /// <see cref="Grants.IPermissionManager.SetAsync(string,string,string,bool,CancellationToken)"/>.
    /// Two registered providers must not share the same name.
    /// </summary>
    string Name { get; }

    /// <summary>Checks the grant status of a single permission for the current user.</summary>
    Task<PermissionGrantResult> CheckAsync(
        PermissionDefinition permission,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    );

    /// <summary>Checks the grant status of multiple permissions for the current user in a single call.</summary>
    Task<MultiplePermissionGrantStatusResult> CheckAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        ICurrentUser currentUser,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Grants (<paramref name="isGranted"/> = <see langword="true"/>) or explicitly prohibits
    /// (<see langword="false"/>) a permission for the provider target identified by
    /// <paramref name="providerKey"/> (for example a user id or role name).
    /// </summary>
    Task SetAsync(
        PermissionDefinition permission,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    /// <summary>Batch overload of <see cref="SetAsync(PermissionDefinition, string, bool, CancellationToken)"/>.</summary>
    Task SetAsync(
        IReadOnlyCollection<PermissionDefinition> permissions,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );
}
