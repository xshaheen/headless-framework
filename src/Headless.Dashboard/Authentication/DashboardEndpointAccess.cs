// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Dashboard;

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
}
