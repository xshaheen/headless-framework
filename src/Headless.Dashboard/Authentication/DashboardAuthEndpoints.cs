// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// The endpoints a dashboard's SPA uses to learn and change its sign-in state: <c>GET api/auth/info</c>,
/// <c>POST api/auth/session</c>, <c>POST api/auth/sign-out</c>, and <c>GET auth/sign-in</c>.
/// </summary>
internal static class DashboardAuthEndpoints
{
    private const string _SignInPath = "/auth/sign-in";
    private const string _AttemptMarker = "attempt";

    // The dashboard SPA reads these field names; the host's own JSON options must not rename them.
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the auth endpoints on <paramref name="dashboard"/>, the route group at the dashboard's base path.</summary>
    internal static void Map(IEndpointRouteBuilder dashboard, string authenticationName)
    {
        dashboard
            .MapGet("/api/auth/info", _Handler(_GetInfoAsync, authenticationName))
            .AllowAnonymous()
            .ExcludeFromDescription();

        dashboard
            .MapPost("/api/auth/session", _Handler(_ExchangeAsync, authenticationName))
            .AllowAnonymous()
            .RequireDashboardRequestIntegrity()
            .ExcludeFromDescription();

        dashboard
            .MapPost("/api/auth/sign-out", _Handler(_SignOutAsync, authenticationName))
            .AllowAnonymous()
            .RequireDashboardRequestIntegrity()
            .ExcludeFromDescription();

        dashboard
            .MapGet(_SignInPath, _Handler(_SignInAsync, authenticationName))
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    // A lambda returning Task<IResult> binds to the RequestDelegate overload, which discards the result.
    private static Func<HttpContext, Task<IResult>> _Handler(
        Func<HttpContext, string, Task<IResult>> handler,
        string authenticationName
    )
    {
        return context => handler(context, authenticationName);
    }

    private static async Task<IResult> _GetInfoAsync(HttpContext context, string authenticationName)
    {
        var auth = _Config(context, authenticationName);
        var mode = auth.Mode.ToString().ToLowerInvariant();

        if (auth.Mode != AuthMode.Host)
        {
            // Basic and API key send their credential on every request, so an anonymous call has no session to report.
            var open = auth.Mode == AuthMode.None;
            return Results.Json(
                new AuthInfoResponse(
                    mode,
                    auth.IsEnabled,
                    SignInAvailable: false,
                    Authenticated: open,
                    Name: null,
                    CanOperate: open
                ),
                _JsonOptions
            );
        }

        var signInAvailable = auth.SignInAvailable;
        var session = await DashboardHostSession.AuthenticateAsync(context, authenticationName).ConfigureAwait(false);
        if (session is not { Succeeded: true, Principal: { } principal })
        {
            return Results.Json(
                new AuthInfoResponse(
                    mode,
                    Enabled: true,
                    signInAvailable,
                    Authenticated: false,
                    Name: null,
                    CanOperate: false
                ),
                _JsonOptions
            );
        }

        var viewPolicy = DashboardHostAuthorization.HostPolicyName(auth.Host, operate: false);
        var operatePolicy = DashboardHostAuthorization.HostPolicyName(auth.Host, operate: true);
        var canOperate =
            await DashboardHostAuthorization
                .PassesHostPolicyAsync(context, principal, session.Properties, viewPolicy)
                .ConfigureAwait(false)
            && (
                string.Equals(viewPolicy, operatePolicy, StringComparison.Ordinal)
                || await DashboardHostAuthorization
                    .PassesHostPolicyAsync(context, principal, session.Properties, operatePolicy)
                    .ConfigureAwait(false)
            );

        return Results.Json(
            new AuthInfoResponse(
                mode,
                Enabled: true,
                signInAvailable,
                Authenticated: true,
                principal.Identity?.Name,
                canOperate
            ),
            _JsonOptions
        );
    }

    private static async Task<IResult> _ExchangeAsync(HttpContext context, string authenticationName)
    {
        var auth = _Config(context, authenticationName);
        if (auth.Mode != AuthMode.Host)
        {
            return Results.NotFound();
        }

        var hostUser = await DashboardHostAuthorization
            .AuthenticateHostUserAsync(context, authenticationName, auth.Host, [null])
            .ConfigureAwait(false);

        if (hostUser is null)
        {
            return Results.Unauthorized();
        }

        return await _IssueAsync(context, authenticationName, auth.Host, hostUser).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> _SignOutAsync(HttpContext context, string authenticationName)
    {
        if (_Config(context, authenticationName).Mode != AuthMode.Host)
        {
            return Results.NotFound();
        }

        await DashboardHostSession.SignOutAsync(context, authenticationName).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> _SignInAsync(HttpContext context, string authenticationName)
    {
        var auth = _Config(context, authenticationName);
        if (auth.Mode != AuthMode.Host)
        {
            return Results.NotFound();
        }

        var signInUrl = (context.Request.PathBase + context.Request.Path).Value!.TrimEnd('/');
        var basePath = signInUrl[..^_SignInPath.Length];
        var returnUrl = _SafeReturnUrl(context.Request.Query["returnUrl"], basePath);

        var challengeScheme = string.IsNullOrEmpty(auth.Host.ChallengeScheme) ? null : auth.Host.ChallengeScheme;
        var hostUser = await DashboardHostAuthorization
            .AuthenticateHostUserAsync(context, authenticationName, auth.Host, [challengeScheme, null])
            .ConfigureAwait(false);

        if (hostUser is not null)
        {
            return await _IssueAsync(context, authenticationName, auth.Host, hostUser).ConfigureAwait(false)
                ? Results.Redirect(returnUrl)
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // The challenge returns here with the marker. Arriving with it and still no host user means the host's
        // sign-in did not produce one the dashboard can read, and challenging again would loop.
        if (challengeScheme is null || context.Request.Query.ContainsKey(_AttemptMarker))
        {
            return Results.Unauthorized();
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = $"{signInUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}&{_AttemptMarker}=1",
        };

        return Results.Challenge(properties, [challengeScheme]);
    }

    private static async Task<bool> _IssueAsync(
        HttpContext context,
        string authenticationName,
        DashboardHostAuthenticationOptions host,
        DashboardHostAuthorization.HostUser hostUser
    )
    {
        // A user the view policy refuses never gets a session, so a session always means "may view".
        var mayView = await DashboardHostAuthorization
            .PassesHostPolicyAsync(
                context,
                hostUser.Principal,
                hostUser.Schemes,
                DashboardHostAuthorization.HostPolicyName(host, operate: false)
            )
            .ConfigureAwait(false);

        if (!mayView)
        {
            return false;
        }

        // A new session counts as freshly revalidated, so a user the host's revalidation now refuses must not get one;
        // otherwise signing in again would undo the revocation for a whole revalidation interval.
        if (host.Revalidate is { } revalidate && !await revalidate(context, hostUser.Principal).ConfigureAwait(false))
        {
            return false;
        }

        await DashboardHostSession
            .SignInAsync(context, authenticationName, host, hostUser.Principal, hostUser.Schemes)
            .ConfigureAwait(false);

        return true;
    }

    // Only a local URL under the dashboard's own base path is followed, so the sign-in page cannot become an open
    // redirect or send the operator elsewhere in the host.
    private static string _SafeReturnUrl(string? returnUrl, string basePath)
    {
        var fallback = basePath + "/";

        if (string.IsNullOrEmpty(returnUrl) || !RedirectHttpResult.IsLocalUrl(returnUrl) || returnUrl[0] != '/')
        {
            return fallback;
        }

        // The browser removes dot segments, including encoded ones, when it follows the redirect, so a path that carries
        // one could leave the base path after passing the prefix check below.
        var pathEnd = returnUrl.IndexOfAny(['?', '#']);
        var path = pathEnd >= 0 ? returnUrl[..pathEnd] : returnUrl;
        if (_HasDotSegment(Uri.UnescapeDataString(path)))
        {
            return fallback;
        }

        var underBase =
            string.Equals(returnUrl, basePath, StringComparison.OrdinalIgnoreCase)
            || returnUrl.StartsWith(fallback, StringComparison.OrdinalIgnoreCase)
            || returnUrl.StartsWith(basePath + "?", StringComparison.OrdinalIgnoreCase)
            || returnUrl.StartsWith(basePath + "#", StringComparison.OrdinalIgnoreCase);

        return underBase ? returnUrl : fallback;
    }

    private static bool _HasDotSegment(string path)
    {
        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment is "." or "..")
            {
                return true;
            }
        }

        return false;
    }

    private static AuthConfig _Config(HttpContext context, string authenticationName)
    {
        return context.RequestServices.GetRequiredService<IOptionsMonitor<AuthConfig>>().Get(authenticationName);
    }

    private sealed record AuthInfoResponse(
        string Mode,
        bool Enabled,
        bool SignInAvailable,
        bool Authenticated,
        string? Name,
        bool CanOperate
    );
}
