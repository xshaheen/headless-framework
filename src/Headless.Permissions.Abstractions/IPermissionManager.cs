// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;

namespace Headless.Permissions;

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
    /// <exception cref="Headless.ConflictException">
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
    /// <exception cref="Headless.ConflictException">
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
    /// Reads what a group of permissions holds for one provider target (for example a role) together with the group's
    /// version, for an edit form that saves it back with <see cref="SetVersionedAsync"/>.
    /// </summary>
    /// <param name="permissionNames">The permissions of the group. Save back exactly this set of names.</param>
    /// <param name="providerName">The grant provider, for example <c>"Role"</c> or <c>"User"</c>.</param>
    /// <param name="providerKey">The target, for example the role name or the user id.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <returns>
    /// One entry per requested permission, read from storage rather than the cache, in the current tenant, and the
    /// group's version. The grants are what the target itself holds, not the user's effective result.
    /// </returns>
    /// <remarks>
    /// The version changes when any permission of the group is granted, prohibited, or cleared for the target, through
    /// any write member. A write that leaves a permission in the state it already had stores nothing and keeps the
    /// version. It is the same for every caller of the same names, target, and tenant, so it serves as an HTTP
    /// <c>ETag</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="permissionNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="permissionNames"/> is empty.</exception>
    /// <exception cref="Headless.ConflictException">
    /// A permission is not defined, is disabled, or does not allow <paramref name="providerName"/>, or no grant
    /// provider with that name is registered.
    /// </exception>
    /// <exception cref="NotSupportedException">The provider's storage cannot version its grants.</exception>
    Task<VersionedPermissionGrants> GetVersionedAsync(
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Saves a group of permissions read with <see cref="GetVersionedAsync"/>, unless any of them changed for the
    /// target after that read.
    /// </summary>
    /// <param name="grants">
    /// The new state keyed by permission name, for exactly the names the version was read for: <see langword="true"/>
    /// grants, <see langword="false"/> explicitly prohibits, and <see langword="null"/> clears the target's record.
    /// </param>
    /// <param name="providerName">The grant provider, for example <c>"Role"</c>.</param>
    /// <param name="providerKey">The target, as passed to <see cref="GetVersionedAsync"/>.</param>
    /// <param name="version">The <see cref="VersionedPermissionGrants.Version"/> the caller read.</param>
    /// <param name="cancellationToken">The abort token.</param>
    /// <remarks>
    /// The check and the write run in one serializable transaction. Of two saves that change the same group from one
    /// version, at most one succeeds. An unchanged save writes nothing and preserves the token. A successful save
    /// announces one <see cref="PermissionGrantChangedMessage"/>. The returned token is computed inside the transaction.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="grants"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="grants"/> is empty, or <paramref name="version"/> is empty or white space.</exception>
    /// <exception cref="Headless.ConflictException">
    /// The group changed after <paramref name="version"/> was read (error code <c>g:permission_version_mismatch</c>,
    /// also raised when the names differ from the ones read), or a permission is not defined, is disabled, or does not
    /// allow <paramref name="providerName"/>, or no grant provider with that name is registered.
    /// </exception>
    /// <exception cref="NotSupportedException">The provider's storage cannot version its grants.</exception>
    Task<string> SetVersionedAsync(
        IReadOnlyDictionary<string, bool?> grants,
        string providerName,
        string providerKey,
        string version,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Removes every permission grant (both grants and explicit prohibitions) recorded for the given provider
    /// target. Typically called when a user or role itself is deleted.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="providerName"/> or <paramref name="providerKey"/> is text some provider would not keep unchanged as a key.
    /// </exception>
    Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default);
}
