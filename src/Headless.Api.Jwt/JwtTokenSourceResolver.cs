// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Headless.Api;

/// <summary>Reads the access token from the sources <see cref="HeadlessJwtBearerOptions.TokenSources"/> enables.</summary>
internal static class JwtTokenSourceResolver
{
    private const string _BearerPrefix = "Bearer ";

    /// <summary>
    /// Wraps <paramref name="events"/>' message-received handler so the configured sources apply after it, letting a
    /// consumer's own handler keep priority: when it sets a token or a result, the sources stay out of the way.
    /// </summary>
    public static void Attach(JwtBearerEvents events, HeadlessJwtBearerOptions options)
    {
        // Header-only is what JwtBearerHandler already does, so the default leaves the events untouched.
        if (options.TokenSources == JwtTokenSources.Header)
        {
            return;
        }

        var inner = events.OnMessageReceived;

        events.OnMessageReceived = async context =>
        {
            await inner(context).ConfigureAwait(false);

            if (context.Token is null && context.Result is null)
            {
                Apply(context, options);
            }
        };
    }

    public static void Apply(MessageReceivedContext context, HeadlessJwtBearerOptions options)
    {
        var token = Resolve(context.HttpContext, options);

        if (token is not null)
        {
            context.Token = token;
        }
        else if (!options.TokenSources.HasFlag(JwtTokenSources.Header))
        {
            // Without a token the handler falls back to the Authorization header; stop it when that source is off.
            context.NoResult();
        }
    }

    /// <summary>Returns the first token found in the order header, cookie, query string, or null.</summary>
    public static string? Resolve(HttpContext context, HeadlessJwtBearerOptions options)
    {
        var request = context.Request;
        var sources = options.TokenSources;

        if (sources.HasFlag(JwtTokenSources.Header) && _FromHeader(request) is { } headerToken)
        {
            return headerToken;
        }

        if (
            sources.HasFlag(JwtTokenSources.Cookie)
            && request.Cookies[options.CookieName] is { Length: > 0 } cookieToken
            // The browser attaches the cookie to a request any site starts, so the cookie proves nothing on its own.
            && SameOriginRequestValidator.IsSameOrigin(context)
        )
        {
            return cookieToken;
        }

        if (
            sources.HasFlag(JwtTokenSources.QueryString)
            && _IsQueryPath(request.Path, options.QueryPathPrefixes)
            && request.Query[options.QueryParameterName] is { Count: 1 } query
            && !string.IsNullOrEmpty(query[0])
        )
        {
            return query[0];
        }

        return null;
    }

    private static string? _FromHeader(HttpRequest request)
    {
        string? authorization = request.Headers[HeaderNames.Authorization];

        if (authorization is null || !authorization.StartsWith(_BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorization[_BearerPrefix.Length..].Trim();

        return token.Length == 0 ? null : token;
    }

    private static bool _IsQueryPath(PathString path, List<string> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
