// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// The dashboard's own session under <see cref="AuthMode.Host"/>: a cookie scheme per dashboard, issued from a host
/// user's identities and claims, with an idle timeout, an absolute lifetime, and optional revalidation.
/// </summary>
/// <remarks>
/// The absolute expiry, the last revalidation time, and the host schemes that authenticated the user live in the
/// ticket's <see cref="AuthenticationProperties.Items"/>, which the cookie encrypts and carries across renewals.
/// <see cref="AuthenticationProperties.IssuedUtc"/> cannot carry the session start: every sliding renewal resets it.
/// </remarks>
internal static class DashboardHostSession
{
    private const string _AbsoluteExpiresKey = ".headless.dashboard.absolute_expires";
    private const string _ValidatedAtKey = ".headless.dashboard.validated_at";
    private const string _HostSchemesKey = ".headless.dashboard.host_schemes";

    /// <summary>The name of the session cookie scheme of the dashboard <paramref name="authenticationName"/>.</summary>
    internal static string SchemeName(string authenticationName) => authenticationName + ".Session";

    /// <summary>The name of the policy that lets a session view the dashboard <paramref name="authenticationName"/>.</summary>
    internal static string ViewPolicyName(string authenticationName) => authenticationName + ".View";

    /// <summary>The name of the policy that lets a session operate the dashboard <paramref name="authenticationName"/>.</summary>
    internal static string OperatePolicyName(string authenticationName) => authenticationName + ".Operate";

    /// <summary>
    /// The default session cookie name: <c>__Host-</c> and the authentication name in lower case, with every character
    /// outside letters, digits, and <c>-</c> replaced by <c>-</c>.
    /// </summary>
    internal static string DefaultCookieName(string authenticationName)
    {
        var name = new StringBuilder("__Host-", 7 + authenticationName.Length);
        foreach (var character in authenticationName)
        {
            name.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        return name.ToString();
    }

    /// <summary>
    /// Registers the session cookie scheme of <paramref name="authenticationName"/> once. The scheme is inert until a
    /// host-mode endpoint names one of the dashboard's session policies.
    /// </summary>
    internal static void AddSessionScheme(IServiceCollection services, string authenticationName)
    {
        var scheme = SchemeName(authenticationName);

        // AddCookie adds the scheme to AuthenticationOptions, which throws on a second scheme of one name when the
        // options are built, so a repeated registration of one dashboard must not reach it twice.
        if (services.Any(descriptor => _IsMarkerFor(descriptor, scheme)))
        {
            return;
        }

        services.AddKeyedSingleton<SessionSchemeMarker>(scheme);
        services.TryAddSingleton(TimeProvider.System);

        new AuthenticationBuilder(services).AddCookie(scheme, displayName: null, static _ => { });

        services
            .AddOptions<CookieAuthenticationOptions>(scheme)
            .Configure<IOptionsMonitor<AuthConfig>>(
                (cookie, configs) => _ConfigureCookie(cookie, authenticationName, configs.Get(authenticationName).Host)
            );
    }

    /// <summary>
    /// Signs <paramref name="hostUser"/> into the session of <paramref name="authenticationName"/>, copying each of its
    /// authenticated identities with their name and role claim types.
    /// </summary>
    internal static async Task SignInAsync(
        HttpContext context,
        string authenticationName,
        DashboardHostAuthenticationOptions host,
        ClaimsPrincipal hostUser,
        IReadOnlyCollection<string> hostSchemes
    )
    {
        var scheme = SchemeName(authenticationName);
        var timeProvider =
            context
                .RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(scheme)
                .TimeProvider
            ?? TimeProvider.System;
        var now = timeProvider.GetUtcNow();

        // The ClaimsIdentity constructor clones each claim onto the new identity. The source's actor and bootstrap
        // context stay behind: a bootstrap context can hold the host's bearer token, which the cookie must not carry.
        var principal = new ClaimsPrincipal(
            hostUser
                .Identities.Where(identity => identity.IsAuthenticated)
                .Select(identity => new ClaimsIdentity(
                    identity.Claims,
                    identity.AuthenticationType,
                    identity.NameClaimType,
                    identity.RoleClaimType
                ))
        );

        var properties = new AuthenticationProperties { IsPersistent = false, AllowRefresh = true };
        properties.Items[_AbsoluteExpiresKey] = _Format(now + host.SessionLifetime);
        properties.Items[_ValidatedAtKey] = _Format(now);
        properties.Items[_HostSchemesKey] = string.Join(',', hostSchemes);

        await context.SignInAsync(scheme, principal, properties).ConfigureAwait(false);
    }

    /// <summary>Deletes the session cookie of <paramref name="authenticationName"/>.</summary>
    internal static Task SignOutAsync(HttpContext context, string authenticationName)
    {
        return context.SignOutAsync(SchemeName(authenticationName));
    }

    /// <summary>
    /// Authenticates the session of <paramref name="authenticationName"/> on <paramref name="context"/>. The cookie
    /// handler keeps its result for the request, so a policy and an endpoint that both ask cost one ticket read.
    /// </summary>
    internal static Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string authenticationName)
    {
        return context.AuthenticateAsync(SchemeName(authenticationName));
    }

    /// <summary>The host schemes that authenticated the user a session was issued for.</summary>
    internal static IReadOnlyCollection<string> GetHostSchemes(AuthenticationProperties? properties)
    {
        return properties?.Items.TryGetValue(_HostSchemesKey, out var schemes) == true && !string.IsNullOrEmpty(schemes)
            ? schemes.Split(',')
            : [];
    }

    private static void _ConfigureCookie(
        CookieAuthenticationOptions cookie,
        string authenticationName,
        DashboardHostAuthenticationOptions host
    )
    {
        var scheme = SchemeName(authenticationName);

        cookie.Cookie.Name = string.IsNullOrEmpty(host.SessionCookieName)
            ? DefaultCookieName(authenticationName)
            : host.SessionCookieName;
        // A __Host- cookie must be Secure, host-only, and Path=/, or the browser drops it.
        cookie.Cookie.Path = "/";
        cookie.Cookie.Domain = null;
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        cookie.Cookie.SameSite = SameSiteMode.Strict;
        cookie.Cookie.IsEssential = true;
        cookie.ExpireTimeSpan = host.SessionIdleTimeout;
        cookie.SlidingExpiration = true;

        cookie.Events = new CookieAuthenticationEvents
        {
            // The dashboard has no login or access-denied page: its API, hub, and SPA expect a status code.
            OnRedirectToLogin = static context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = static context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            OnCheckSlidingExpiration = context =>
            {
                if (_Now(context.Options) >= _AbsoluteExpires(context.Properties))
                {
                    context.ShouldRenew = false;
                }

                return Task.CompletedTask;
            },
            OnValidatePrincipal = context => _ValidatePrincipalAsync(context, scheme, host),
        };
    }

    private static async Task _ValidatePrincipalAsync(
        CookieValidatePrincipalContext context,
        string scheme,
        DashboardHostAuthenticationOptions host
    )
    {
        var now = _Now(context.Options);

        if (now >= _AbsoluteExpires(context.Properties))
        {
            await _EndAsync(context, scheme).ConfigureAwait(false);
            return;
        }

        if (host.Revalidate is not { } revalidate || host.RevalidationInterval is not { } interval)
        {
            return;
        }

        var validatedAt = _Parse(context.Properties, _ValidatedAtKey) ?? DateTimeOffset.MinValue;
        if (now - validatedAt < interval)
        {
            return;
        }

        if (!await revalidate(context.HttpContext, context.Principal!).ConfigureAwait(false))
        {
            await _EndAsync(context, scheme).ConfigureAwait(false);
            return;
        }

        context.Properties.Items[_ValidatedAtKey] = _Format(now);
        context.ShouldRenew = true;
    }

    private static Task _EndAsync(CookieValidatePrincipalContext context, string scheme)
    {
        // Rejecting fails this request only; signing out also deletes the cookie the browser would keep sending.
        context.RejectPrincipal();
        return context.HttpContext.SignOutAsync(scheme);
    }

    private static DateTimeOffset _Now(CookieAuthenticationOptions options)
    {
        return (options.TimeProvider ?? TimeProvider.System).GetUtcNow();
    }

    // A ticket without an absolute expiry was not issued by this session, so it is treated as already expired.
    private static DateTimeOffset _AbsoluteExpires(AuthenticationProperties properties)
    {
        return _Parse(properties, _AbsoluteExpiresKey) ?? DateTimeOffset.MinValue;
    }

    private static DateTimeOffset? _Parse(AuthenticationProperties properties, string key)
    {
        return
            properties.Items.TryGetValue(key, out var value)
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed
            )
            ? parsed
            : null;
    }

    private static string _Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static bool _IsMarkerFor(ServiceDescriptor descriptor, string scheme)
    {
        return descriptor.ServiceType == typeof(SessionSchemeMarker)
            && descriptor.IsKeyedService
            && string.Equals(descriptor.ServiceKey as string, scheme, StringComparison.Ordinal);
    }

    /// <summary>Records in the service collection that one dashboard's session scheme is registered.</summary>
    private sealed class SessionSchemeMarker;
}
