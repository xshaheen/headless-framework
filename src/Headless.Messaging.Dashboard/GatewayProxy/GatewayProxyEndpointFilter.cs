// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;

namespace Headless.Messaging.Dashboard.GatewayProxy;

/// <summary>
/// Endpoint filter that intercepts requests and delegates to the gateway proxy agent
/// when the request targets a different node. Applied via <c>.AddEndpointFilter&lt;&gt;()</c>
/// on the proxied endpoint group, replacing the old per-endpoint inline check.
/// </summary>
public sealed class GatewayProxyEndpointFilter(GatewayProxyAgent? agent) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        if (agent is not null)
        {
            var httpContext = context.HttpContext;
            if (await agent.Invoke(httpContext))
            {
                // Proxy handled it — return empty result to skip endpoint handler
                return Results.Empty;
            }
        }

        // No proxy or proxy didn't handle it — continue to endpoint
        return await next(context);
    }
}
