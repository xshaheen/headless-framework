// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Permissions;

/// <summary>
/// Defines the enlisted grant writes behind <c>unit.Permissions</c>: every read and write of a call runs inside the
/// unit's own transaction, so the unit's rollback discards the change and its commit makes it durable.
/// </summary>
/// <remarks>
/// A singleton registered by <c>AddHeadlessPermissions</c>. It holds no unit: the caller's handle arrives per call,
/// and the call refuses, before any write, a unit that is no longer active, carries no live relational transaction,
/// or targets another database than the configured storage. The grant cache is cleared and
/// <see cref="PermissionGrantChangedMessage" /> is published only after the unit commits; a rolled-back unit does
/// neither. Application code reaches it through <c>unit.Permissions</c> rather than directly.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkPermissions : IUnitOfWorkFeature
{
    /// <summary>Grants or explicitly denies one permission inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the write.</param>
    /// <param name="permissionName">The permission name.</param>
    /// <param name="providerName">The grant provider name, for example <c>"User"</c> or <c>"Role"</c>.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="isGranted"><see langword="true" /> to grant; <see langword="false" /> to write an explicit denial.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> is <see langword="null" />.</exception>
    /// <exception cref="ConflictException">The permission or provider is not defined, or the permission is disabled.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    Task SetAsync(
        IUnitOfWork unitOfWork,
        string permissionName,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    /// <summary>Grants or explicitly denies several permissions inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the write.</param>
    /// <param name="permissionNames">The permission names.</param>
    /// <param name="providerName">The grant provider name, for example <c>"User"</c> or <c>"Role"</c>.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="isGranted"><see langword="true" /> to grant; <see langword="false" /> to write explicit denials.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the writes ran inside the unit's transaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> is <see langword="null" />.</exception>
    /// <exception cref="ConflictException">A permission or the provider is not defined, or a permission is disabled.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    Task SetAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyCollection<string> permissionNames,
        string providerName,
        string providerKey,
        bool isGranted,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes every grant and denial of one provider subject inside <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the delete.</param>
    /// <param name="providerName">The grant provider name.</param>
    /// <param name="providerKey">The provider-specific subject key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the delete ran inside the unit's transaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the delete.</exception>
    /// <exception cref="NotSupportedException">The configured storage cannot write inside a unit of work.</exception>
    Task DeleteAsync(
        IUnitOfWork unitOfWork,
        string providerName,
        string providerKey,
        CancellationToken cancellationToken = default
    );
}
