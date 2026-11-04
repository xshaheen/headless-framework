// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Applies a dashboard's access rule to endpoints mounted on the host's endpoint routing, where the host's
/// <c>UseAuthorization</c> evaluates it before the endpoint or a SignalR hub runs.
/// </summary>
internal static class DashboardEndpointAccess
{
    /// <summary>
    /// Registers the dashboard's authentication scheme and the authorization policy of the same name that requires a
    /// user that scheme signed in. Repeated calls for one name register it once.
    /// </summary>
    internal static void AddDashboardScheme(IServiceCollection services, string authenticationName)
    {
        services.AddAuthentication();
        services.AddAuthorization();
        services.TryAddTransient<DashboardAuthenticationHandler>();

        services.Configure<AuthenticationOptions>(options =>
        {
            if (!options.SchemeMap.ContainsKey(authenticationName))
            {
                options.AddScheme<DashboardAuthenticationHandler>(authenticationName, displayName: null);
            }
        });

        services.Configure<AuthorizationOptions>(options =>
        {
            if (options.GetPolicy(authenticationName) is null)
            {
                options.AddPolicy(
                    authenticationName,
                    policy => policy.AddAuthenticationSchemes(authenticationName).RequireAuthenticatedUser()
                );
            }
        });
    }

    /// <summary>
    /// Names the dashboard's CORS policy on <paramref name="builder"/> only when one was configured: the host's pipeline
    /// runs <c>UseCors</c> only if the host adds it, and an endpoint with CORS metadata and no CORS middleware throws
    /// at request time.
    /// </summary>
    internal static TBuilder RequireDashboardCors<TBuilder>(this TBuilder builder, string policyName, bool configured)
        where TBuilder : IEndpointConventionBuilder
    {
        return configured ? builder.RequireCors(policyName) : builder;
    }

    /// <summary>
    /// Protects <paramref name="builder"/>'s endpoints the way <paramref name="auth"/> says: anonymous under
    /// <see cref="AuthMode.None"/>, the host's named policy (or its default policy) under <see cref="AuthMode.Host"/>,
    /// and the dashboard's own scheme and policy under Basic, API key, or custom authentication. Each sets authorization
    /// metadata, so the host's fallback policy never decides a dashboard request.
    /// </summary>
    internal static TBuilder RequireDashboardAccess<TBuilder>(
        this TBuilder builder,
        AuthConfig auth,
        string authenticationName
    )
        where TBuilder : IEndpointConventionBuilder
    {
        switch (auth.Mode)
        {
            case AuthMode.None:
                return builder.AllowAnonymous();
            case AuthMode.Host when string.IsNullOrEmpty(auth.HostAuthorizationPolicy):
                return builder.RequireAuthorization();
            case AuthMode.Host:
                return builder.RequireAuthorization(auth.HostAuthorizationPolicy!);
            default:
                builder.WithMetadata(new DashboardEndpointMetadata(authenticationName));
                return builder.RequireAuthorization(authenticationName);
        }
    }
}
