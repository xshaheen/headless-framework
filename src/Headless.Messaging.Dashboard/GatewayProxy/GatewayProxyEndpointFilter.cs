// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard.GatewayProxy;

/// <summary>
/// Endpoint filter that intercepts requests and delegates to the gateway proxy agent
/// when the request targets a different node. Applied via <c>.AddEndpointFilter&lt;&gt;()</c>
/// on the proxied endpoint group, replacing the old per-endpoint inline check.
/// </summary>
/// <remarks>
/// Uses <see cref="IServiceProvider"/> instead of direct <see cref="GatewayProxyAgent"/> injection
/// because <c>AddEndpointFilter&lt;T&gt;()</c> uses <c>ActivatorUtilities.CreateInstance</c>,
/// which cannot resolve optional/nullable parameters. The agent is only registered when
/// K8s or Consul discovery is configured.
/// </remarks>
public sealed class GatewayProxyEndpointFilter(IServiceProvider serviceProvider) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var agent = serviceProvider.GetService<GatewayProxyAgent>();
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
