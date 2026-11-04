// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Endpoint filter that intercepts requests and delegates to the gateway proxy agent
/// when the request targets a different node. Applied via <c>.AddEndpointFilter&lt;&gt;()</c>
/// on the proxied endpoint group, replacing the old per-endpoint inline check.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="IServiceProvider"/> instead of direct <see cref="GatewayProxyAgent"/> injection
/// because <c>AddEndpointFilter&lt;T&gt;()</c> uses <c>ActivatorUtilities.CreateInstance</c>,
/// which cannot resolve optional/nullable parameters. The agent is only registered when
/// K8s or Consul discovery is configured.
/// </para>
/// <para>
/// Under host authentication the request is always served by this node, and a node selection the browser still
/// carries is cleared. The proxy forwards the request's own credential, and a host session is a cookie scoped to
/// this host, so another node has no way to trust it.
/// </para>
/// </remarks>
public sealed class GatewayProxyEndpointFilter(IServiceProvider serviceProvider) : IEndpointFilter
{
    private static readonly string[] _NodeSelectionCookies =
    [
        GatewayProxyAgent.CookieNodeName,
        GatewayProxyAgent.CookieNodeNsName,
    ];

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (_IsHostMode())
        {
            _ClearNodeSelection(httpContext);
            return await next(context).ConfigureAwait(false);
        }

        var agent = serviceProvider.GetService<GatewayProxyAgent>();
        if (agent is not null && await agent.Invoke(httpContext).ConfigureAwait(false))
        {
            return Results.Empty;
        }

        // No proxy or proxy didn't handle it — continue to endpoint
        return await next(context).ConfigureAwait(false);
    }

    private bool _IsHostMode()
    {
        return serviceProvider
                .GetRequiredService<IOptionsMonitor<AuthConfig>>()
                .Get(MessagingDashboardOptionsBuilder.AuthenticationName)
                .Mode == AuthMode.Host;
    }

    private static void _ClearNodeSelection(HttpContext httpContext)
    {
        foreach (var name in _NodeSelectionCookies)
        {
            if (httpContext.Request.Cookies.ContainsKey(name))
            {
                httpContext.Response.Cookies.Delete(name);
            }
        }
    }
}
