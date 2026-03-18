// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Headless.Messaging.Dashboard.GatewayProxy;

public class GatewayProxyAgent(
    ILoggerFactory loggerFactory,
    IRequestMapper requestMapper,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IServiceProvider serviceProvider,
    INodeDiscoveryProvider discoveryProvider
)
{
    public const string CookieNodeName = "messaging.node";
    public const string CookieNodeNsName = "messaging.node.ns";

    private readonly ConsulDiscoveryOptions? _consulDiscoveryOptions =
        serviceProvider.GetService<ConsulDiscoveryOptions>();
    private readonly ILogger _logger = loggerFactory.CreateLogger<GatewayProxyAgent>();

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
                    node = await discoveryProvider.GetNode(requestNodeName, ns);
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
            if (_consulDiscoveryOptions.NodeName == requestNodeName)
            {
                return false;
            }

            if (!cache.TryGetValue(requestNodeName, out node))
            {
                node = await discoveryProvider.GetNode(requestNodeName);
                cache.Set(requestNodeName, node);
            }
        }

        if (node != null)
        {
            try
            {
                var downstreamRequest = await requestMapper.Map(request);

                _SetDownStreamRequestUri(
                    downstreamRequest,
                    node,
                    request.Path.Value ?? string.Empty,
                    request.QueryString.Value ?? string.Empty
                );

                using var client = httpClientFactory.CreateClient("GatewayProxy");
                using var response = await client.SendAsync(downstreamRequest, context.RequestAborted);

                await _SetResponseOnHttpContext(context, response);

                return true;
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

    private static async Task _SetResponseOnHttpContext(HttpContext context, HttpResponseMessage response)
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
            await response.Content.CopyToAsync(context.Response.Body);
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
            context.Response.Headers.Append(
                httpResponseHeader.Key,
                new StringValues(httpResponseHeader.Value.ToArray())
            );
        }
    }
}
