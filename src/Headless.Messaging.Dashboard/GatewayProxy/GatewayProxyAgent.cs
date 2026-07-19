// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Headless.Messaging.Dashboard.GatewayProxy;

/// <summary>
/// Reverse-proxy agent that forwards dashboard API requests to a remote messaging node.
/// Activated when the incoming request carries the <c>messaging.node</c> cookie identifying
/// a peer node that is different from the current node. Uses the configured
/// <see cref="INodeDiscoveryProvider"/> to resolve the peer's address.
/// </summary>
internal sealed class GatewayProxyAgent(
    ILoggerFactory loggerFactory,
    IRequestMapper requestMapper,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IServiceProvider serviceProvider,
    INodeDiscoveryProvider discoveryProvider
)
{
    /// <summary>Cookie name that carries the target node's service name.</summary>
    public const string CookieNodeName = "messaging.node";

    /// <summary>Cookie name that carries the Kubernetes namespace of the target node.</summary>
    public const string CookieNodeNsName = "messaging.node.ns";

    private readonly ConsulDiscoveryOptions? _consulDiscoveryOptions =
        serviceProvider.GetService<ConsulDiscoveryOptions>();
    private readonly ILogger _logger = loggerFactory.CreateLogger<GatewayProxyAgent>();

    /// <summary>
    /// Inspects the incoming request for a <c>messaging.node</c> cookie and, when present,
    /// resolves the target node via the discovery provider and proxies the request to it.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>
    /// <see langword="true"/> if the request was forwarded to a peer node;
    /// <see langword="false"/> if the cookie is absent, the node could not be resolved, or the
    /// target is the current node itself (Consul path only).
    /// </returns>
    public async Task<bool> Invoke(HttpContext context)
    {
        var request = context.Request;
        var isSwitchNode = request.Cookies.TryGetValue(CookieNodeName, out var requestNodeName);
        if (!isSwitchNode)
        {
            return false;
        }

        _logger.LogCallingRemoteEndpoint();

        if (requestNodeName == null)
        {
            return false;
        }

        Node? node;
        if (_consulDiscoveryOptions == null) // it's k8s
        {
            if (request.Cookies.TryGetValue(CookieNodeNsName, out var ns))
            {
                var cacheKey = $"{requestNodeName}\0{ns}";
                if (!cache.TryGetValue(cacheKey, out node))
                {
                    node = await discoveryProvider
                        .GetNodeAsync(requestNodeName, ns, context.RequestAborted)
                        .ConfigureAwait(false);
                    cache.Set(cacheKey, node);
                }
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (string.Equals(_consulDiscoveryOptions.NodeName, requestNodeName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!cache.TryGetValue(requestNodeName, out node))
            {
                node = await discoveryProvider
                    .GetNodeAsync(requestNodeName, cancellationToken: context.RequestAborted)
                    .ConfigureAwait(false);
                cache.Set(requestNodeName, node);
            }
        }

        if (node != null)
        {
            try
            {
                var downstreamRequest = await requestMapper.Map(request).ConfigureAwait(false);

                _SetDownStreamRequestUri(
                    downstreamRequest,
                    node,
                    request.Path.Value ?? string.Empty,
                    request.QueryString.Value ?? string.Empty
                );

                using var client = httpClientFactory.CreateClient("GatewayProxy");
                using var response = await client
                    .SendAsync(downstreamRequest, context.RequestAborted)
                    .ConfigureAwait(false);

                await _SetResponseOnHttpContext(context, response, context.RequestAborted).ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogErrorInvokingDownstreamNode(ex);
            }
        }
        else
        {
            context.Response.Cookies.Delete(CookieNodeName);
        }

        return false;
    }

    private static async Task _SetResponseOnHttpContext(
        HttpContext context,
        HttpResponseMessage response,
        CancellationToken cancellationToken
    )
    {
        foreach (var httpResponseHeader in response.Content.Headers)
        {
            _AddHeaderIfDoesntExist(context, httpResponseHeader);
        }

        context.Response.OnStarting(
            state =>
            {
                var httpContext = (HttpContext)state;

                httpContext.Response.StatusCode = (int)response.StatusCode;

                return Task.CompletedTask;
            },
            context
        );

        if (response.StatusCode != HttpStatusCode.NotModified)
        {
            await response.Content.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void _SetDownStreamRequestUri(
        HttpRequestMessage downstreamRequest,
        Node node,
        string requestPath,
        string queryString
    )
    {
        var uriBuilder = !node.Address.StartsWith("http", StringComparison.Ordinal)
            ? new UriBuilder("http://", node.Address, node.Port, requestPath, queryString)
            : new UriBuilder(node.Address + requestPath + queryString);

        if (node.Port > 0)
        {
            uriBuilder.Port = node.Port;
        }

        downstreamRequest.RequestUri = uriBuilder.Uri;
    }

    private static void _AddHeaderIfDoesntExist(
        HttpContext context,
        KeyValuePair<string, IEnumerable<string>> httpResponseHeader
    )
    {
        if (!context.Response.Headers.ContainsKey(httpResponseHeader.Key))
        {
            context.Response.Headers.Append(httpResponseHeader.Key, new StringValues([.. httpResponseHeader.Value]));
        }
    }
}
