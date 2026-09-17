// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Reads a raw, caller-supplied tenant identifier (for example a hostname label, route segment, or
/// header value) from the current HTTP request. Registered sources are consulted in registration order
/// by <c>TenantCatalogResolutionMiddleware</c> — the first <see cref="TenantIdentifierSourceResult"/> of
/// kind <see cref="TenantIdentifierSourceResultKind.Found"/> wins.
/// </summary>
/// <remarks>
/// Implementations should be synchronous and side-effect free: reading a header, host label, or route
/// value requires no I/O. The returned identifier is raw, caller-controlled input — normalization
/// (trimming, lowercasing) and shape validation are owned entirely by <c>ITenantCatalogService</c>,
/// never by the source. A source that finds no identifier returns
/// <see cref="TenantIdentifierSourceResult.None"/>; a source whose input is present but ambiguous —
/// for example a tenant header repeated with two different values — returns
/// <see cref="TenantIdentifierSourceResult.Invalid"/>, which rejects the request immediately with the
/// catalog's invalid-identifier outcome before any store call (R5).
/// </remarks>
[PublicAPI]
public interface ITenantIdentifierSource
{
    /// <summary>Reads a raw tenant identifier from the current request, if present.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>
    /// <see cref="TenantIdentifierSourceResult.None"/> when this source found no identifier (absent,
    /// empty, or whitespace-only input), <see cref="TenantIdentifierSourceResult.Found"/> with the raw
    /// identifier when it found one, or <see cref="TenantIdentifierSourceResult.Invalid"/> when the
    /// input is present but too ambiguous to interpret.
    /// </returns>
    TenantIdentifierSourceResult GetIdentifier(HttpContext context);
}
