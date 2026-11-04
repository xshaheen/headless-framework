// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Applies the host's view and operate policies to a dashboard session, and finds the host user a session is issued
/// for.
/// </summary>
/// <remarks>
/// A dashboard endpoint lists only the session scheme. Listing a host scheme beside it would make every listed
/// scheme answer a failed request, the host's sign-in redirect included. The host policy is therefore evaluated
/// against the session's copied claims through <see cref="IAuthorizationService"/>, which authenticates nothing and
/// ignores the policy's schemes; a policy that lists schemes still passes only for a session issued from one of them.
/// </remarks>
internal static class DashboardHostAuthorization
{
    /// <summary>Registers the view and operate policies of <paramref name="authenticationName"/> once.</summary>
    internal static void AddSessionPolicies(IServiceCollection services, string authenticationName)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, HostPolicyHandler>());

        services.Configure<AuthorizationOptions>(options =>
        {
            _AddPolicy(options, authenticationName, operate: false);
            _AddPolicy(options, authenticationName, operate: true);
        });
    }

    /// <summary>The host policy that view (or operate) evaluates; <see langword="null"/> means the default policy.</summary>
    internal static string? HostPolicyName(DashboardHostAuthenticationOptions host, bool operate)
    {
        if (operate && !string.IsNullOrEmpty(host.OperatePolicy))
        {
            return host.OperatePolicy;
        }

        return string.IsNullOrEmpty(host.ViewPolicy) ? null : host.ViewPolicy;
    }

    /// <summary>
    /// Whether <paramref name="user"/>, authenticated by <paramref name="authenticatedSchemes"/>, passes the host policy
    /// named <paramref name="policyName"/>, or the host's default policy when it is <see langword="null"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host registers no policy named <paramref name="policyName"/>.</exception>
    internal static Task<bool> PassesHostPolicyAsync(
        HttpContext context,
        ClaimsPrincipal user,
        IReadOnlyCollection<string> authenticatedSchemes,
        string? policyName
    )
    {
        return _PassesHostPolicyAsync(context, user, authenticatedSchemes, sessionProperties: null, policyName);
    }

    /// <summary>
    /// Whether the session <paramref name="user"/>, whose ticket carries <paramref name="sessionProperties"/>, passes
    /// the host policy named <paramref name="policyName"/>, or the host's default policy when it is
    /// <see langword="null"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host registers no policy named <paramref name="policyName"/>.</exception>
    internal static Task<bool> PassesHostPolicyAsync(
        HttpContext context,
        ClaimsPrincipal user,
        AuthenticationProperties? sessionProperties,
        string? policyName
    )
    {
        return _PassesHostPolicyAsync(context, user, authenticatedSchemes: null, sessionProperties, policyName);
    }

    private static async Task<bool> _PassesHostPolicyAsync(
        HttpContext context,
        ClaimsPrincipal user,
        IReadOnlyCollection<string>? authenticatedSchemes,
        AuthenticationProperties? sessionProperties,
        string? policyName
    )
    {
        var policy = await _GetHostPolicyAsync(context, policyName).ConfigureAwait(false);

        // A policy that names its schemes trusts only users those schemes authenticated; a session issued from any
        // other host scheme must not inherit that trust through copied claims. Only such a policy reads the schemes.
        if (policy.AuthenticationSchemes.Count > 0)
        {
            var schemes = authenticatedSchemes ?? DashboardHostSession.GetHostSchemes(sessionProperties);
            if (!policy.AuthenticationSchemes.Any(schemes.Contains))
            {
                return false;
            }
        }

        var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(user, context, policy).ConfigureAwait(false);

        return result.Succeeded;
    }

    /// <summary>
    /// Authenticates the host user a session is issued for. The schemes the view policy lists are authenticated
    /// together when it lists any; otherwise each of <paramref name="fallbackSchemes"/> is tried in turn, a
    /// <see langword="null"/> entry standing for the host's default authenticate scheme.
    /// </summary>
    internal static async Task<HostUser?> AuthenticateHostUserAsync(
        HttpContext context,
        string authenticationName,
        DashboardHostAuthenticationOptions host,
        IEnumerable<string?> fallbackSchemes
    )
    {
        var viewPolicy = await _GetHostPolicyAsync(context, HostPolicyName(host, operate: false)).ConfigureAwait(false);

        if (viewPolicy.AuthenticationSchemes.Count > 0)
        {
            var identities = new List<ClaimsIdentity>();
            var schemes = new List<string>();
            foreach (var scheme in viewPolicy.AuthenticationSchemes)
            {
                var result = await context.AuthenticateAsync(scheme).ConfigureAwait(false);
                if (result is { Succeeded: true, Principal: { } principal })
                {
                    identities.AddRange(principal.Identities);
                    schemes.Add(scheme);
                }
            }

            return schemes.Count == 0 ? null : new HostUser(new ClaimsPrincipal(identities), schemes);
        }

        var schemeProvider = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        foreach (var candidate in fallbackSchemes)
        {
            var scheme = string.IsNullOrEmpty(candidate)
                ? (await schemeProvider.GetDefaultAuthenticateSchemeAsync().ConfigureAwait(false))?.Name
                : candidate;

            if (
                scheme is null
                || _IsDashboardScheme(
                    await schemeProvider.GetSchemeAsync(scheme).ConfigureAwait(false),
                    authenticationName
                )
            )
            {
                continue;
            }

            var result = await context.AuthenticateAsync(scheme).ConfigureAwait(false);
            if (result is { Succeeded: true, Principal: { } principal })
            {
                return new HostUser(principal, [scheme]);
            }
        }

        return null;
    }

    // The dashboard's own schemes never prove who the host user is: the session would vouch for itself.
    private static bool _IsDashboardScheme(AuthenticationScheme? scheme, string authenticationName)
    {
        return scheme is null
            || scheme.HandlerType == typeof(DashboardAuthenticationHandler)
            || string.Equals(
                scheme.Name,
                DashboardHostSession.SchemeName(authenticationName),
                StringComparison.Ordinal
            );
    }

    private static async Task<AuthorizationPolicy> _GetHostPolicyAsync(HttpContext context, string? policyName)
    {
        var provider = context.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>();

        if (policyName is null)
        {
            return await provider.GetDefaultPolicyAsync().ConfigureAwait(false);
        }

        return await provider.GetPolicyAsync(policyName).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The dashboard names the host authorization policy '{policyName}', which the host does not register."
            );
    }

    private static void _AddPolicy(AuthorizationOptions options, string authenticationName, bool operate)
    {
        var name = operate
            ? DashboardHostSession.OperatePolicyName(authenticationName)
            : DashboardHostSession.ViewPolicyName(authenticationName);

        if (options.GetPolicy(name) is not null)
        {
            return;
        }

        options.AddPolicy(
            name,
            policy =>
                policy
                    .AddAuthenticationSchemes(DashboardHostSession.SchemeName(authenticationName))
                    .RequireAuthenticatedUser()
                    .AddRequirements(new HostPolicyRequirement(authenticationName, operate))
        );
    }

    /// <summary>A host user found for session issuance and the schemes that authenticated it.</summary>
    internal sealed record HostUser(ClaimsPrincipal Principal, IReadOnlyCollection<string> Schemes);

    /// <summary>Requires the session principal to pass the dashboard's host view or operate policy.</summary>
    private sealed record HostPolicyRequirement(string AuthenticationName, bool Operate) : IAuthorizationRequirement;

    /// <summary>Succeeds a <see cref="HostPolicyRequirement"/> when the session passes the host policy.</summary>
    /// <remarks>
    /// <see cref="IAuthorizationService"/> comes from the request's services, not the constructor: the service depends
    /// on every registered handler, so a handler that takes it in its constructor forms a dependency cycle.
    /// </remarks>
    private sealed class HostPolicyHandler(IOptionsMonitor<AuthConfig> configs)
        : AuthorizationHandler<HostPolicyRequirement>
    {
        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            HostPolicyRequirement requirement
        )
        {
            if (context.Resource is not HttpContext httpContext)
            {
                return;
            }

            var host = configs.Get(requirement.AuthenticationName).Host;
            var session = await DashboardHostSession
                .AuthenticateAsync(httpContext, requirement.AuthenticationName)
                .ConfigureAwait(false);

            if (
                await PassesHostPolicyAsync(
                        httpContext,
                        context.User,
                        session.Properties,
                        HostPolicyName(host, requirement.Operate)
                    )
                    .ConfigureAwait(false)
            )
            {
                context.Succeed(requirement);
            }
        }
    }
}
