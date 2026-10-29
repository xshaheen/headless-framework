// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Headless.Api;

/// <summary>
/// The one same-origin decision that the endpoint filter, the MVC attribute, and the JWT cookie source share, so a
/// request a guarded endpoint refuses can never authenticate through a cookie either.
/// </summary>
internal static class SameOriginRequestValidator
{
    internal const string SecFetchSiteHeaderName = "Sec-Fetch-Site";

    /// <summary>Returns a 403 problem carrying <c>g:cross_site_request</c>, or null when the request passes.</summary>
    public static ProblemDetails? Validate(HttpContext context)
    {
        if (IsSameOrigin(context))
        {
            return null;
        }

        var error = GeneralMessageDescriber.CrossSiteRequest();

        return context.RequestServices.GetRequiredService<IProblemDetailsCreator>().Forbidden(error.Description, error);
    }

    public static bool IsSameOrigin(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<HeadlessSameOriginOptions>>().Value;

        return IsSameOrigin(context.Request, options);
    }

    /// <summary>
    /// Applies the rules in order and fails closed: an unknown, repeated, or contradictory header value refuses the
    /// request rather than being read leniently.
    /// </summary>
    public static bool IsSameOrigin(HttpRequest request, HeadlessSameOriginOptions options)
    {
        var headers = request.Headers;
        var origin = headers[HeaderNames.Origin];
        var fetchSite = headers[SecFetchSiteHeaderName];
        var hasOrigin = !StringValues.IsNullOrEmpty(origin);
        var hasFetchSite = !StringValues.IsNullOrEmpty(fetchSite);

        // Without either header nothing shows that a page on this origin sent the request.
        if (!hasOrigin && !hasFetchSite)
        {
            return false;
        }

        var originIsAllowed = false;

        if (hasOrigin)
        {
            if (origin.Count != 1)
            {
                return false;
            }

            var value = origin[0]!;
            var isSelf = string.Equals(value, _RequestOrigin(request), StringComparison.OrdinalIgnoreCase);
            originIsAllowed = !isSelf && _IsAllowed(value, options.AllowedOrigins);

            if (!isSelf && !originIsAllowed)
            {
                return false;
            }
        }

        if (hasFetchSite)
        {
            if (fetchSite.Count != 1)
            {
                return false;
            }

            var site = fetchSite[0];

            // 'same-site' covers every subdomain of the registrable domain, including ones the API does not trust, so
            // it passes only with an Origin the application listed. 'cross-site', 'none' (a typed URL or bookmark),
            // and any value a future browser adds are refused.
            var siteAllowed =
                string.Equals(site, "same-origin", StringComparison.Ordinal)
                || (string.Equals(site, "same-site", StringComparison.Ordinal) && originIsAllowed);

            if (!siteAllowed)
            {
                return false;
            }
        }

        return options.RequiredHeaderName is not { } requiredHeader
            || !string.IsNullOrWhiteSpace(headers[requiredHeader].ToString());
    }

    /// <summary>
    /// Serializes the request's own origin the way a browser writes the <c>Origin</c> header: punycode host and no
    /// default port, so a <c>Host</c> header that spells out <c>:443</c> still matches.
    /// </summary>
    private static string _RequestOrigin(HttpRequest request)
    {
        var host = request.Host;
        var defaultPort = string.Equals(request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? 443
            : 80;
        var authority = host.Port == defaultPort ? new HostString(host.Host).ToUriComponent() : host.ToUriComponent();

        return request.Scheme + "://" + authority;
    }

    private static bool _IsAllowed(string origin, List<string> allowedOrigins)
    {
        foreach (var allowed in allowedOrigins)
        {
            if (string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
