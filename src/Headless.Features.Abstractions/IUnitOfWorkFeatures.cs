// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.UnitOfWork;

namespace Headless.Features;

/// <summary>
/// Defines the enlisted feature value writes behind <c>unit.Features</c>: every read and write of a call, the inherited
/// fallback comparison included, runs inside the unit's own transaction, so the unit's rollback discards the change and
/// its commit makes it durable.
/// </summary>
/// <remarks>
/// A singleton registered by <c>AddHeadlessFeatures</c>. It holds no unit: the caller's handle arrives per call, and
/// the call refuses, before any write, a unit that is no longer active, carries no live relational transaction, or
/// targets another database than the configured storage. The value cache is cleared and
/// <see cref="FeatureChangedMessage" /> is published only after the unit commits; a rolled-back unit does neither.
/// Application code reaches it through <c>unit.Features</c> rather than directly.
/// </remarks>
[PublicAPI]
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUnitOfWorkFeatures : IUnitOfWorkFeature
{
    /// <summary>Writes several feature values for one provider scope inside <paramref name="unitOfWork" />'s transaction.</summary>
    /// <param name="unitOfWork">The unit whose transaction holds the write.</param>
    /// <param name="values">The feature names and values; a <see langword="null" /> value clears the value.</param>
    /// <param name="providerName">The value provider name, for example <c>"Edition"</c> or <c>"Tenant"</c>.</param>
    /// <param name="providerKey">The provider-specific key, such as a tenant or edition id.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store a value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the writes ran inside the unit's transaction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="unitOfWork" /> or <paramref name="values" /> is <see langword="null" />.</exception>
    /// <exception cref="ConflictException">A feature is not defined, or the provider is not registered or is read-only.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    Task SetAsync(
        IUnitOfWork unitOfWork,
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes every value stored for one provider scope inside <paramref name="unitOfWork" />'s transaction.
    /// </summary>
    /// <param name="unitOfWork">The unit whose transaction holds the delete.</param>
    /// <param name="providerName">The value provider name.</param>
    /// <param name="providerKey">The provider-specific key.</param>
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
