// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Reads a raw, caller-supplied tenant identifier (for example a hostname label, route segment, or
/// header value) from the current HTTP request. Implemented by the deferred host/route/header
/// identifier strategies; v1 ships no built-in source. Registered sources are consulted in
/// registration order by <c>TenantCatalogResolutionMiddleware</c> — the first non-<see langword="null"/>
/// identifier wins.
/// </summary>
/// <remarks>
/// Implementations should be synchronous and side-effect free: reading a header, host label, or route
/// value requires no I/O. The returned value is raw, caller-controlled input — normalization, shape
/// validation, and store lookup are owned entirely by <c>ITenantCatalogService</c>, never by the source.
/// </remarks>
[PublicAPI]
public interface ITenantIdentifierSource
{
    /// <summary>Reads a raw tenant identifier from the current request, if present.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The raw identifier, or <see langword="null"/> when this source found none.</returns>
    string? GetIdentifier(HttpContext context);
}
