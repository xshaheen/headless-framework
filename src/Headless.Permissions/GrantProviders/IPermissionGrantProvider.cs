// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.UnitOfWork;

namespace Headless.Permissions;

/// <summary>
/// Plug-in point for a single grant resolution strategy. The framework ships <see cref="UserPermissionGrantProvider"/>
/// and <see cref="RolePermissionGrantProvider"/> and calls each registered provider during resolution.
/// An explicit <c>Prohibited</c> result from any provider overrides grants from all others.
/// </summary>
/// <remarks>
/// New members arrive as default interface members. <see cref="SetEnlistedAsync" /> defaults to throwing
/// <see cref="NotSupportedException" />, so <c>unit.Permissions</c> refuses a provider that cannot write inside a unit of
/// work; <see cref="StorePermissionGrantProvider" /> implements it.
/// </remarks>
public interface IPermissionGrantProvider
{
    /// <summary>
    /// Unique name that identifies this provider (e.g. <c>"User"</c> or <c>"Role"</c>).
    /// Used as the <c>providerName</c> argument to <see cref="IPermissionGrantStore"/> and
    /// <see cref="IPermissionManager.SetAsync(string,string,string,bool,CancellationToken)"/>.
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

    /// <summary>
    /// Grants or explicitly prohibits <paramref name="permissions"/> for the provider target inside
    /// <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the writes.</param>
    /// <param name="permissions">The permissions to write.</param>
    /// <param name="providerKey">The provider target.</param>
    /// <param name="isGranted"><see langword="true" /> to grant; <see langword="false" /> to prohibit.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">The unit cannot host the writes.</exception>
    /// <exception cref="NotSupportedException">This provider cannot write inside a unit of work.</exception>
    Task SetEnlistedAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<PermissionDefinition> permissions,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken
    )
    {
        throw new NotSupportedException(
            $"The permission grant provider '{Name}' cannot write inside a unit of work. Write through the injected "
                + "IPermissionManager, or implement SetEnlistedAsync."
        );
    }
}
