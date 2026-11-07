// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard;

/// <summary>
/// Refuses a live stream request whose <c>Origin</c> header names another origin than the request's own. Every
/// session cookie is host-only, so the dashboard is served from one origin in every authentication mode.
/// </summary>
/// <remarks>
/// A sibling subdomain is same-site, so the browser still sends it the <c>SameSite=Strict</c> session cookie on a
/// stream request; refusing a foreign <c>Origin</c> stops such a page from reading the dashboard's live events. A
/// non-browser client sends no <c>Origin</c> and passes.
/// </remarks>
internal static class SameOriginCheck
{
    /// <summary>
    /// Whether <paramref name="request"/> carries no <c>Origin</c> header, or one naming the request's own scheme,
    /// host, and port.
    /// </summary>
    public static bool IsAllowed(HttpRequest request)
    {
        var origins = request.Headers.Origin;
        if (origins.Count == 0)
        {
            return true;
        }

        if (
            origins.Count != 1
            || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            || !request.Host.HasValue
        )
        {
            return false;
        }

        var requestPort =
            request.Host.Port
            ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);

        return string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(origin.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == requestPort;
    }

    /// <summary>
    /// The refusal's body: it names the origin the server saw for itself, because the usual cause is a reverse proxy
    /// whose forwarded scheme and host the host does not apply.
    /// </summary>
    public static string RefusalMessage(HttpRequest request)
    {
        return $"The dashboard stream refuses the cross-origin request from '{request.Headers.Origin}', because this "
            + $"server sees its own origin as '{request.Scheme}://{request.Host}'. Behind a reverse proxy or load "
            + "balancer, apply its forwarded headers (UseForwardedHeaders with X-Forwarded-Proto and X-Forwarded-Host) "
            + "before the dashboard, so the server sees the origin the browser used.";
    }
}
