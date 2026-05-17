// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

        var mainHostBaseUri = new Uri(mainHost, UriKind.Absolute);

        app.MapGet(
                pattern: "{*path}",
                handler: (HttpContext context) =>
                {
                    // Build the redirect target from a trusted base URI so the host
                    // cannot be hijacked via crafted paths (e.g. "//evil.com/foo").
                    var builder = new UriBuilder
                    {
                        Scheme = mainHostBaseUri.Scheme,
                        Host = mainHostBaseUri.Host,
                        Port = mainHostBaseUri.IsDefaultPort ? -1 : mainHostBaseUri.Port,
                        Path = context.Request.Path.Value ?? string.Empty,
                        Query = context.Request.QueryString.Value?.TrimStart('?') ?? string.Empty,
                    };

                    context.Response.Redirect(builder.Uri.ToString(), permanent: true);

                    return ValueTask.CompletedTask;
                }
            )
            .RequireHost(redirectHosts);
    }
}
