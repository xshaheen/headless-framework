// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.Models;

namespace Headless.Permissions.Grants;

/// <summary>
/// Resolves and mutates permission grants for a principal. Resolution is AWS IAM-style: an explicit
/// <c>Prohibited</c> from any grant provider denies the permission regardless of other grants, a permission
/// is granted when at least one provider grants it and none denies it, and the default is deny.
/// </summary>
public interface IPermissionManager
{
    /// <summary>
    /// Resolves the effective grant of a single permission for <paramref name="currentUser"/>.
    /// </summary>
    /// <param name="permissionName">The unique permission name to evaluate.</param>
    /// <param name="currentUser">The principal whose grants (user id, roles, etc.) drive resolution.</param>
    /// <param name="providerName">
    /// When set (for example <see cref="PermissionGrantProviderNames.User"/> or
    /// <see cref="PermissionGrantProviderNames.Role"/>), restricts evaluation to that single grant provider;
    /// when <see langword="null"/>, all registered providers participate.
    /// </param>
    /// <returns>
    /// The resolution result. An undefined or disabled permission resolves to not-granted rather than
    /// throwing, so callers can rely on <see cref="GrantedPermissionResult.IsGranted"/> directly.
    /// </returns>
    Task<GrantedPermissionResult> GetAsync(
        string permissionName,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Resolves the effective grant of every defined permission for <paramref name="currentUser"/>.
    /// </summary>
    /// <param name="providerName">When set, restricts evaluation to that single grant provider; otherwise all providers participate.</param>
    Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Resolves the effective grant of the given permissions for <paramref name="currentUser"/>. Names that are
    /// not defined are returned as not-granted (they do not throw), so the result always contains one entry per
    /// requested name.
    /// </summary>
    /// <param name="providerName">When set, restricts evaluation to that single grant provider; otherwise all providers participate.</param>
    Task<IReadOnlyList<GrantedPermissionResult>> GetAllAsync(
        IReadOnlyCollection<string> permissionNames,
        ICurrentUser currentUser,
        string? providerName = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Grants (<paramref name="isGranted"/> = <see langword="true"/>) or explicitly prohibits
    /// (<see langword="false"/>) a permission for a provider target, identified by <paramref name="providerName"/>
    /// (e.g. <c>"User"</c>/<c>"Role"</c>) and <paramref name="providerKey"/> (the user id, role name, etc.).
    /// </summary>
    /// <exception cref="Headless.Exceptions.ConflictException">
    /// Thrown when the permission is not defined, is disabled, restricts its providers and does not allow
    /// <paramref name="providerName"/>, or when no grant provider with that name is registered.
    /// </exception>
    Task SetAsync(
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Batch overload of <see cref="SetAsync(string, string, string, bool, CancellationToken)"/> applying the same
    /// grant/prohibit state to every name. The change is rejected as a whole (no partial application) if any name
    /// fails validation.
    /// </summary>
    /// <exception cref="Headless.Exceptions.ConflictException">
    /// Thrown when any permission is not defined, is disabled, restricts its providers and does not allow
    /// <paramref name="providerName"/>, or when no grant provider with that name is registered.
    /// </exception>
    Task SetAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Removes every permission grant (both grants and explicit prohibitions) recorded for the given provider
    /// target. Typically called when a user or role itself is deleted.
    /// </summary>
    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
