// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Tenant identifier source reading a route value (R2): yields the string route value named by
/// <see cref="RouteTenantIdentifierSourceOptions.RouteValueName"/> (default <c>tenant</c>) from
/// <c>Request.RouteValues</c>, carried raw and unchanged.
/// </summary>
/// <remarks>
/// <para>
/// Depends on routing having run: <c>RouteValues</c> is populated by endpoint routing, so this
/// source finds nothing when <c>UseHeadlessTenantCatalogResolution()</c> is placed before
/// <c>UseRouting()</c> — every request then runs as host context, which the middleware's
/// route-source misorder event makes loud (R10).
/// </para>
/// <para>
/// A missing or non-string value (a route default of another type, an absent segment) yields
/// <see cref="TenantIdentifierSourceResult.None"/> — the value is never coerced through
/// <c>ToString()</c>, because a non-string route value is not a caller-supplied identifier. A
/// whitespace-only string normalizes to <see cref="TenantIdentifierSourceResult.None"/> at
/// construction of the result. No trimming, lowercasing, or shape validation happens here;
/// <c>ITenantCatalogService</c> owns normalization (R6).
/// </para>
/// </remarks>
internal sealed class RouteTenantIdentifierSource(IOptions<RouteTenantIdentifierSourceOptions> options)
    : ITenantIdentifierSource
{
    private readonly string _routeValueName = options.Value.RouteValueName;

    /// <inheritdoc/>
    public TenantIdentifierSourceResult GetIdentifier(HttpContext context)
    {
        Argument.IsNotNull(context);

        // A non-string route value (a typed route default, an int constraint) is not an identifier:
        // never coerce it — only a caller-supplied string segment is one (R2).
        return context.Request.RouteValues[_routeValueName] is string identifier
            ? TenantIdentifierSourceResult.Found(identifier)
            : TenantIdentifierSourceResult.None;
    }
}
