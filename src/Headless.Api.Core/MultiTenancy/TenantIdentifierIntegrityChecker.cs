// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.MultiTenancy;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// The single R19 comparison rule shared by every enforcement point: the pre-auth catalog check in
/// <c>TenantCatalogResolutionMiddleware</c>, the post-authorization
/// <see cref="TenantIdentifierIntegrityHandler"/>, and the claim-resolution fast path in
/// <c>TenantResolutionMiddleware</c>. A principal without a tenant claim is never a mismatch — R19
/// constrains claim-carrying requests only, and claim-free requests keep the store-free path (R8).
/// </summary>
internal static class TenantIdentifierIntegrityChecker
{
    /// <summary>
    /// Determines whether <paramref name="principal"/> carries a tenant claim that canonicalizes to a
    /// different tenant than the identifier-resolved <paramref name="resolvedTenantId"/>.
    /// </summary>
    /// <param name="principal">The authenticated principal, or <see langword="null"/> when none was materialized.</param>
    /// <param name="resolvedTenantId">The canonical tenant id produced by identifier-based catalog resolution.</param>
    /// <param name="options">The multi-tenancy options carrying the configured claim type.</param>
    /// <returns><see langword="true"/> when the claim and the resolved identifier disagree.</returns>
    internal static bool IsMismatch(ClaimsPrincipal? principal, string resolvedTenantId, MultiTenancyOptions options)
    {
        if (principal is null)
        {
            return false;
        }

        var claimTenantId = TenantClaimReader.GetTenantId(principal, options);

        return claimTenantId is not null && !string.Equals(claimTenantId, resolvedTenantId, StringComparison.Ordinal);
    }
}
