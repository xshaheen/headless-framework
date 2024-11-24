// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Flurl;
using Microsoft.AspNetCore.Http;

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

        app.MapGet(
                pattern: "{*path}",
                handler: (HttpContext context, string? path) =>
                {
                    var location = mainHost;

                    if (path is not null)
                    {
                        location = Url.Combine(location, path);
                    }

                    context.Response.Redirect(location, permanent: true);

                    return ValueTask.CompletedTask;
                }
            )
            .RequireHost(redirectHosts);
    }
}
