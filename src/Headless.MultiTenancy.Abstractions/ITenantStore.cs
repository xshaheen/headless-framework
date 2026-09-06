// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Read-only service-provider interface for looking up tenants. Deliberately minimal — two lookups,
/// no writes. The catalog service in the family Core owns normalization, ignored-identifier
/// filtering, shape validation, and caching; stores never see raw caller input.
/// </summary>
/// <remarks>
/// Implementing this interface over an app-owned tenant aggregate (rather than the shipped stores) is
/// a documented first-class path — the in-memory, configuration, and Entity Framework Core stores this
/// framework ships are convenience defaults, not a canonical schema apps must adopt.
/// <para>
/// Both lookups must return an instance the caller may mutate freely: the catalog service hands a
/// store result straight to application code on a cache miss, and <see cref="TenantInfo.ExtraProperties"/>
/// is a mutable bag. An implementation backed by an in-process cache or a seeded dictionary must
/// therefore materialize a fresh <see cref="TenantInfo"/> per call rather than alias its own state.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITenantStore
{
    /// <summary>Finds the tenant whose identifier equals <paramref name="normalizedIdentifier"/>.</summary>
    /// <param name="normalizedIdentifier">
    /// The identifier to look up, already normalized (trimmed, lowercased) by the catalog service.
    /// Implementations compare this value ordinally and must not re-normalize it.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="TenantInfo"/>, or <see langword="null"/> when no tenant has that identifier.</returns>
    Task<TenantInfo?> FindByIdentifierAsync(string normalizedIdentifier, CancellationToken cancellationToken = default);

    /// <summary>Finds the tenant whose canonical identifier equals <paramref name="id"/>.</summary>
    /// <param name="id">The canonical tenant identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The matching <see cref="TenantInfo"/>, or <see langword="null"/> when no tenant has that id.</returns>
    Task<TenantInfo?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
}
