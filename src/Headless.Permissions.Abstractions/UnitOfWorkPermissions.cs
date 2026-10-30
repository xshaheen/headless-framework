// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Permissions;

/// <summary>
/// Provides one unit-of-work handle bound to permission grant writes, returned by <c>unit.Permissions</c>. A grant,
/// denial, or delete made through it runs inside that unit's transaction, so it is kept only when the unit commits.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.Permissions</c> and kept as unit-local state; it owns
/// nothing to dispose. The grant cache is cleared and <see cref="PermissionGrantChangedMessage" /> is published after
/// the unit commits, never at write time. The injected <see cref="IPermissionManager" /> stays autonomous: its writes
/// never join a unit.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkPermissions
{
    private readonly IUnitOfWorkPermissions _permissions;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkPermissions(IUnitOfWorkPermissions permissions, IUnitOfWork unitOfWork)
    {
        _permissions = permissions;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Grants or explicitly denies one permission inside the bound unit's transaction.</summary>
    /// <param name="permissionName">The permission name.</param>
    /// <param name="providerName">The grant provider name, for example <c>"User"</c> or <c>"Role"</c>.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="isGranted"><see langword="true" /> to grant; <see langword="false" /> to write an explicit denial.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    /// <exception cref="ConflictException">The permission or provider is not defined, or the permission is disabled.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    public Task SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return _permissions.SetAsync(
            _unitOfWork,
            permissionName,
            providerName,
            providerKey,
            isGranted,
            cancellationToken
        );
    }

    /// <summary>Grants or explicitly denies several permissions inside the bound unit's transaction.</summary>
    /// <param name="permissionNames">The permission names.</param>
    /// <param name="providerName">The grant provider name, for example <c>"User"</c> or <c>"Role"</c>.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="isGranted"><see langword="true" /> to grant; <see langword="false" /> to write explicit denials.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the writes ran inside the unit's transaction.</returns>
    /// <exception cref="ConflictException">A permission or the provider is not defined, or a permission is disabled.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    public Task SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    )
    {
        return _permissions.SetAsync(
            _unitOfWork,
            permissionNames,
            providerName,
            providerKey,
            isGranted,
            cancellationToken
        );
    }

    /// <summary>Deletes every grant and denial of one provider subject inside the bound unit's transaction.</summary>
    /// <param name="providerName">The grant provider name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the delete ran inside the unit's transaction.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot host the delete.</exception>
    /// <exception cref="NotSupportedException">The configured storage cannot write inside a unit of work.</exception>
    public Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default)
    {
        return _permissions.DeleteAsync(_unitOfWork, providerName, providerKey, cancellationToken);
    }

    /// <summary>Grants a permission to a user inside the bound unit's transaction.</summary>
    /// <param name="permissionName">The permission name.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task GrantToUserAsync(string permissionName, string userId, CancellationToken cancellationToken = default)
    {
        return SetAsync(permissionName, PermissionGrantProviderNames.User, userId, isGranted: true, cancellationToken);
    }

    /// <summary>Writes an explicit denial of a permission for a user inside the bound unit's transaction.</summary>
    /// <param name="permissionName">The permission name.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task RevokeFromUserAsync(string permissionName, string userId, CancellationToken cancellationToken = default)
    {
        return SetAsync(permissionName, PermissionGrantProviderNames.User, userId, isGranted: false, cancellationToken);
    }

    /// <summary>Grants a permission to a role inside the bound unit's transaction.</summary>
    /// <param name="permissionName">The permission name.</param>
    /// <param name="roleName">The role name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task GrantToRoleAsync(string permissionName, string roleName, CancellationToken cancellationToken = default)
    {
        return SetAsync(
            permissionName,
            PermissionGrantProviderNames.Role,
            roleName,
            isGranted: true,
            cancellationToken
        );
    }

    /// <summary>Writes an explicit denial of a permission for a role inside the bound unit's transaction.</summary>
    /// <param name="permissionName">The permission name.</param>
    /// <param name="roleName">The role name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task RevokeFromRoleAsync(
        string permissionName,
        string roleName,
        CancellationToken cancellationToken = default
    )
    {
        return SetAsync(
            permissionName,
            PermissionGrantProviderNames.Role,
            roleName,
            isGranted: false,
            cancellationToken
        );
    }
}
