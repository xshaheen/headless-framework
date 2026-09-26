// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.MultiTenancy;

/// <summary>
/// Resolves where a tenant's data physically lives, keyed by canonical tenant id. The framework consults it only
/// for data contexts that were explicitly registered as tenant-routed; every other context keeps the shared
/// database and schema.
/// </summary>
/// <remarks>
/// <para>
/// Physical placement is infrastructure configuration, so it lives behind this seam rather than on
/// <see cref="TenantInfo"/> (which owns identity only) or in Settings (whose EF store is itself a data context and
/// would have to be read before the placement it decides).
/// </para>
/// <para>
/// Implementations must read host-level storage only. A resolver that queries a tenant-routed context would need
/// the placement it is resolving.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITenantDataPlacementResolver
{
    /// <summary>Resolves the data placement of the tenant whose canonical id is <paramref name="tenantId"/>.</summary>
    /// <param name="tenantId">The canonical tenant id.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The tenant's placement, or <see langword="null"/> when the tenant has none. A routed context refuses a
    /// tenant with no placement rather than falling back to the shared database.
    /// </returns>
    Task<TenantDataPlacement?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default);
}
