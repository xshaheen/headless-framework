// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Checks;
using Headless.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.MultiTenancy;

/// <summary>
/// Resolves a raw, caller-supplied tenant identifier into a <see cref="TenantResolutionOutcome"/>, and
/// loads <see cref="TenantInfo"/> by canonical id. HTTP-agnostic — consumed by the pre-auth identifier
/// resolution seam and by <see cref="ICurrentTenantInfo"/>. Owns normalization, shape validation,
/// ignored-identifier filtering, and read-through caching; stores never see raw caller input.
/// </summary>
[PublicAPI]
public interface ITenantCatalogService
{
    /// <summary>
    /// Resolves a raw tenant identifier: normalize → shape-validate → ignored-check → cache/store lookup.
    /// </summary>
    /// <param name="identifier">The raw, caller-supplied identifier (for example a hostname label).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Exactly one <see cref="TenantResolutionOutcome"/> classifying the result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The store answered with a tenant whose <see cref="TenantInfo.Identifier"/> differs from the normalized
    /// identifier, or whose <see cref="TenantInfo.Id"/> differs from the id the cached mapping named. Treated as a
    /// store fault: nothing is cached and the exception is not mapped to a resolution outcome.
    /// </exception>
    Task<TenantResolutionOutcome> ResolveAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads <see cref="TenantInfo"/> for a canonical tenant id through the same cache the identifier
    /// resolution path uses. Never rejects on a disabled tenant — rejection is a resolution-time concern
    /// only.
    /// </summary>
    /// <param name="id">The canonical tenant id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="TenantInfo"/>, or <see langword="null"/> when <paramref name="id"/> has no catalog row.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The store answered with a tenant whose <see cref="TenantInfo.Id"/> differs from <paramref name="id"/>.
    /// Treated as a store fault: nothing is cached.
    /// </exception>
    Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
}
