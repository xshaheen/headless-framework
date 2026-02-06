// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Messaging.Dashboard.GatewayProxy.Requester;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Headless.Messaging.Dashboard.GatewayProxy;

public class GatewayProxyAgent(
    ILoggerFactory loggerFactory,
    IRequestMapper requestMapper,
    IHttpRequester requester,
    IServiceProvider serviceProvider,
    INodeDiscoveryProvider discoveryProvider
)
{
    public const string CookieNodeName = "messaging.node";
    public const string CookieNodeNsName = "messaging.node.ns";

    private readonly ConsulDiscoveryOptions _consulDiscoveryOptions =
        serviceProvider.GetRequiredService<ConsulDiscoveryOptions>();
    private readonly ILogger _logger = loggerFactory.CreateLogger<GatewayProxyAgent>();

    protected HttpRequestMessage? DownstreamRequest { get; set; }

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
                if (MessagingCache.Global.TryGet(requestNodeName + ns, out var nodeObj))
                {
                    node = (Node)nodeObj;
                }
                else
                {
                    node = await discoveryProvider.GetNode(requestNodeName, ns);
                    MessagingCache.Global.AddOrUpdate(requestNodeName + ns, node);
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

            if (MessagingCache.Global.TryGet(requestNodeName, out var nodeObj))
            {
                node = (Node)nodeObj;
            }
            else
            {
                node = await discoveryProvider.GetNode(requestNodeName);
                MessagingCache.Global.AddOrUpdate(requestNodeName, node);
            }
        }

        if (node != null)
        {
            try
            {
                DownstreamRequest = await requestMapper.Map(request);

                _SetDownStreamRequestUri(
                    node,
                    request.Path.Value ?? string.Empty,
                    request.QueryString.Value ?? string.Empty
                );

                var response = await requester.GetResponse(DownstreamRequest);

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

        var content = await response.Content.ReadAsByteArrayAsync();

        _AddHeaderIfDoesntExist(
            context,
            new KeyValuePair<string, IEnumerable<string>>(
                "Content-Length",
                [content.Length.ToString(CultureInfo.InvariantCulture)]
            )
        );

        context.Response.OnStarting(
            state =>
            {
                var httpContext = (HttpContext)state;

                httpContext.Response.StatusCode = (int)response.StatusCode;

                return Task.CompletedTask;
            },
            context
        );

        await using Stream stream = new MemoryStream(content);
        if (response.StatusCode != HttpStatusCode.NotModified)
        {
            await stream.CopyToAsync(context.Response.Body);
        }
    }

    private void _SetDownStreamRequestUri(Node node, string requestPath, string queryString)
    {
        var uriBuilder = !node.Address.StartsWith("http", StringComparison.Ordinal)
            ? new UriBuilder("http://", node.Address, node.Port, requestPath, queryString)
            : new UriBuilder(node.Address + requestPath + queryString);

        if (node.Port > 0)
        {
            uriBuilder.Port = node.Port;
        }

        DownstreamRequest?.RequestUri = uriBuilder.Uri;
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
