// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
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
                handler: (HttpContext context) =>
                {
                    var redirectUri = BuildRedirectUri(
                        mainHostBaseUri,
                        context.Request.Path,
                        context.Request.QueryString
                    );

                    if (
                        redirectUri.Host == mainHostBaseUri.Host
                        && redirectUri.Scheme == mainHostBaseUri.Scheme
                        && redirectUri.Port == mainHostBaseUri.Port
                    )
                    {
                        context.Response.Redirect(redirectUri.AbsoluteUri, permanent: true);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    }

                    return ValueTask.CompletedTask;
                }
            )
            .RequireHost(redirectHosts);
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
}
