// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Immutable per-request feature set by <c>TenantCatalogResolutionMiddleware</c> when identifier-based
/// resolution resolves an enabled tenant. Carries the identifier-resolved canonical tenant id so R19
/// integrity enforcement (<c>TenantIdentifierIntegrityHandler</c>, and the claim-resolution fast path in
/// <c>TenantResolutionMiddleware</c>) can compare it against the authenticated tenant claim directly —
/// never against the ambient <c>ICurrentTenant.Id</c>, which claim resolution may have already
/// overwritten.
/// </summary>
internal sealed class TenantIdentifierResolvedFeature(string tenantId)
{
    /// <summary>The identifier-resolved canonical tenant id.</summary>
    public string TenantId { get; } = tenantId;
}
