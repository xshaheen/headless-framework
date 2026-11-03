// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Enforces a dashboard's own credential (Basic, API key, or custom) on an endpoint group mounted on the host's
/// endpoint routing, where no <see cref="AuthMiddleware"/> branch runs. Returns <c>401 Unauthorized</c> on failure.
/// </summary>
/// <param name="authenticationName">The name the dashboard passed to <c>AddDashboardAuthentication</c>.</param>
internal sealed class DashboardAuthenticationEndpointFilter(string authenticationName) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // The AuthMiddleware category, so a host's log filter for dashboard authentication failures covers both mounts.
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<AuthMiddleware>>();

        if (!await DashboardRequestAuthentication.TryAuthenticateAsync(httpContext, authenticationName, logger))
        {
            return Results.Text("Unauthorized", statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context).ConfigureAwait(false);
    }
}
