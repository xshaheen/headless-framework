// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public static class EndpointsExtensions
{
    public static void RedirectHosts(this WebApplication app, string mainHost, string[]? redirectHosts)
    {
        if (redirectHosts is not { Length: > 0 })
        {
            return;
        }

        var mainHostBaseUri = new Uri(mainHost, UriKind.Absolute);

        app.MapGet(
                pattern: "{*path}",
                handler: (HttpContext context, IProblemDetailsCreator problemDetailsCreator) =>
                    BuildRedirectResultOrBadRequest(
                        context.Request.Path,
                        context.Request.QueryString,
                        mainHostBaseUri,
                        problemDetailsCreator
                    )
            )
            .RequireHost(redirectHosts);
    }

    /// <summary>
    /// Builds a permanent redirect to <paramref name="mainHostBaseUri"/> preserving the original
    /// path and query, or returns a normalized 400 <c>ProblemDetails</c> when the resulting URI
    /// does not match <paramref name="mainHostBaseUri"/> on scheme + host + port. The mismatch
    /// branch guards against open-redirect attempts that smuggle a different host in the path.
    /// </summary>
    internal static IResult BuildRedirectResultOrBadRequest(
        PathString requestPath,
        QueryString queryString,
        Uri mainHostBaseUri,
        IProblemDetailsCreator problemDetailsCreator
    )
    {
        var redirectUri = BuildRedirectUri(mainHostBaseUri, requestPath, queryString);
        return BuildRedirectResultOrBadRequest(redirectUri, mainHostBaseUri, problemDetailsCreator);
    }

    /// <summary>
    /// Pure helper: returns a permanent redirect when <paramref name="redirectUri"/> matches
    /// <paramref name="mainHostBaseUri"/> on scheme + host + port, otherwise returns a 400
    /// <c>ProblemDetails</c> built by <paramref name="problemDetailsCreator"/>. Kept separate so
    /// the mismatch branch is reachable from unit tests without depending on
    /// <see cref="BuildRedirectUri"/> producing a structurally-different host.
    /// </summary>
    internal static IResult BuildRedirectResultOrBadRequest(
        Uri redirectUri,
        Uri mainHostBaseUri,
        IProblemDetailsCreator problemDetailsCreator
    )
    {
        // Defense-in-depth: this branch is unreachable in production because BuildRedirectUri
        // constructs the absolute URI from mainHostBaseUri.Scheme/Host/Port directly. Keep the
        // check so consumers who modify BuildRedirectUri to allow alternate hosts still get the
        // open-redirect guard. Do not remove without auditing all BuildRedirectUri call sites.
        if (!_IsHostMatch(redirectUri, mainHostBaseUri))
        {
            return Results.Problem(problemDetailsCreator.BadRequest());
        }

        return Results.Redirect(redirectUri.AbsoluteUri, permanent: true);
    }

    internal static Uri BuildRedirectUri(Uri mainHostBaseUri, PathString requestPath, QueryString requestQuery)
    {
        var host = mainHostBaseUri.IsDefaultPort
            ? new HostString(mainHostBaseUri.Host)
            : new HostString(mainHostBaseUri.Host, mainHostBaseUri.Port);

        return new Uri(
            UriHelper.BuildAbsolute(mainHostBaseUri.Scheme, host, path: requestPath, query: requestQuery),
            UriKind.Absolute
        );
    }

    private static bool _IsHostMatch(Uri redirectUri, Uri mainHostBaseUri)
    {
        return string.Equals(redirectUri.Host, mainHostBaseUri.Host, StringComparison.Ordinal)
            && string.Equals(redirectUri.Scheme, mainHostBaseUri.Scheme, StringComparison.Ordinal)
            && redirectUri.Port == mainHostBaseUri.Port;
    }
}
