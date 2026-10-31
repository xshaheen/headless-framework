// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Features;

/// <summary>
/// Provides one unit-of-work handle bound to feature value writes, returned by <c>unit.Features</c>. A value written or
/// deleted through it is kept only when that unit commits.
/// </summary>
/// <remarks>
/// One binding per unit, created on the first read of <c>unit.Features</c> and kept as unit-local state; it owns
/// nothing to dispose. The value cache is cleared and <see cref="FeatureChangedMessage" /> is published after the unit
/// commits, never at write time. The injected <see cref="IFeatureManager" /> stays autonomous: its writes never join a
/// unit.
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkFeatures
{
    private readonly IUnitOfWorkFeatures _features;
    private readonly IUnitOfWork _unitOfWork;

    internal UnitOfWorkFeatures(IUnitOfWorkFeatures features, IUnitOfWork unitOfWork)
    {
        _features = features;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Writes one feature value inside the bound unit's transaction.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="value">The value; <see langword="null" /> clears the value for the scope.</param>
    /// <param name="providerName">The value provider name, for example <c>"Edition"</c> or <c>"Tenant"</c>.</param>
    /// <param name="providerKey">The provider-specific key, such as a tenant or edition id.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store the value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    /// <exception cref="ConflictException">The feature is not defined, or the provider is not registered or is read-only.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    public Task SetAsync(
        string name,
        string? value,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal) { [name] = value };

        return _features.SetAsync(_unitOfWork, values, providerName, providerKey, forceToSet, cancellationToken);
    }

    /// <summary>Writes several feature values for one provider scope inside the bound unit's transaction.</summary>
    /// <param name="values">The feature names and values; a <see langword="null" /> value clears the value.</param>
    /// <param name="providerName">The value provider name.</param>
    /// <param name="providerKey">The provider-specific key.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store a value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the writes ran inside the unit's transaction.</returns>
    /// <exception cref="ConflictException">A feature is not defined, or the provider is not registered or is read-only.</exception>
    /// <exception cref="InvalidOperationException">The unit cannot host the write.</exception>
    /// <exception cref="NotSupportedException">The provider or the configured storage cannot write inside a unit of work.</exception>
    public Task SetAsync(
        IReadOnlyDictionary<string, string?> values,
        string providerName,
        string? providerKey,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return _features.SetAsync(_unitOfWork, values, providerName, providerKey, forceToSet, cancellationToken);
    }

    /// <summary>Writes a feature value for a tenant inside the bound unit's transaction.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="value">The value; <see langword="null" /> clears the tenant's value.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store the value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task SetForTenantAsync(
        string name,
        string? value,
        string tenantId,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return SetAsync(name, value, FeatureValueProviderNames.Tenant, tenantId, forceToSet, cancellationToken);
    }

    /// <summary>Enables a feature for a tenant inside the bound unit's transaction, storing <c>"true"</c>.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task GrantToTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
    {
        return SetAsync(name, "true", FeatureValueProviderNames.Tenant, tenantId, forceToSet: true, cancellationToken);
    }

    /// <summary>Disables a feature for a tenant inside the bound unit's transaction, storing <c>"false"</c>.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="tenantId">The tenant id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task RevokeFromTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
    {
        return SetAsync(name, "false", FeatureValueProviderNames.Tenant, tenantId, forceToSet: true, cancellationToken);
    }

    /// <summary>Writes a feature value for an edition inside the bound unit's transaction.</summary>
    /// <param name="name">The feature name.</param>
    /// <param name="value">The value; <see langword="null" /> clears the edition's value.</param>
    /// <param name="editionId">The edition id.</param>
    /// <param name="forceToSet">
    /// <see langword="true" /> to store the value even when it equals the inherited fallback value.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the write ran inside the unit's transaction.</returns>
    public Task SetForEditionAsync(
        string name,
        string? value,
        string editionId,
        bool forceToSet = false,
        CancellationToken cancellationToken = default
    )
    {
        return SetAsync(name, value, FeatureValueProviderNames.Edition, editionId, forceToSet, cancellationToken);
    }

    /// <summary>Deletes every value stored for one provider scope inside the bound unit's transaction.</summary>
    /// <param name="providerName">The value provider name.</param>
    /// <param name="providerKey">The provider-specific key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the delete ran inside the unit's transaction.</returns>
    /// <exception cref="InvalidOperationException">The unit cannot host the delete.</exception>
    /// <exception cref="NotSupportedException">A writable provider or the configured storage cannot write inside a unit of work.</exception>
    public Task DeleteAsync(string providerName, string providerKey, CancellationToken cancellationToken = default)
    {
        return _features.DeleteAsync(_unitOfWork, providerName, providerKey, cancellationToken);
    }
}
