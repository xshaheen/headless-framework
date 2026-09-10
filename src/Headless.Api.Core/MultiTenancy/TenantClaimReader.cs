// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Constants;
using Headless.MultiTenancy;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Shared claim-type resolution used by both the R19 post-authorization integrity check
/// (<see cref="TenantIdentifierIntegrityHandler"/>) and the claim-resolution fast path
/// (<c>TenantResolutionMiddleware</c>): resolves <see cref="MultiTenancyOptions.ClaimType"/> — falling
/// back to <see cref="UserClaimTypes.TenantId"/> when unset — and reads the tenant id from the matching
/// claim.
/// </summary>
internal static class TenantClaimReader
{
    /// <summary>Reads the tenant id from <paramref name="principal"/> using <paramref name="options"/>'s configured claim type.</summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <param name="options">The multi-tenancy options carrying the configured claim type.</param>
    /// <returns>The claim value, or <see langword="null"/> when the claim is absent or whitespace-only.</returns>
    internal static string? GetTenantId(ClaimsPrincipal principal, MultiTenancyOptions options)
    {
        var claimType = string.IsNullOrWhiteSpace(options.ClaimType) ? UserClaimTypes.TenantId : options.ClaimType;

        var value = string.Equals(claimType, UserClaimTypes.TenantId, StringComparison.Ordinal)
            ? principal.GetTenantId()
            : principal.FindFirst(claimType)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
