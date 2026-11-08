// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Sends a read of the module's API for another node to that node and returns the node's answer as it is: the node
/// named by <c>?node=</c>, or the node a message id names. A dashboard-only host sends a read without a node to the
/// first node discovery lists.
/// </summary>
/// <remarks>
/// A node discovery does not list is answered with <c>404</c> before any call leaves this node. A peer that does not
/// answer is answered with <c>502</c>, its state in <c>X-Headless-Node-State</c> and in the problem's detail; this
/// node's own rows never stand in for it. A call another node forwarded is always served here.
/// </remarks>
internal sealed class NodeRelayFilter(MessagingForwarding forwarding, IOptions<HeadlessDashboardOptions> dashboard)
    : IEndpointFilter
{
    private const string _NodeParameter = "node";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (MessagingForwarding.IsForwarded(http.User))
        {
            return await next(context).ConfigureAwait(false);
        }

        var node = _NodeOf(http.Request);

        if (node is null)
        {
            if (!forwarding.IsStandalone)
            {
                return await next(context).ConfigureAwait(false);
            }

            var first = await forwarding.DefaultPeerAsync(http.RequestAborted).ConfigureAwait(false);

            return first is null
                ? Results.Problem(
                    detail: "No Messaging node reachable. " + forwarding.Directory.DiscoveryState,
                    statusCode: StatusCodes.Status503ServiceUnavailable
                )
                : await _RelayAsync(http, first).ConfigureAwait(false);
        }

        if (forwarding.Directory.IsLocal(node) && !forwarding.IsStandalone)
        {
            return await next(context).ConfigureAwait(false);
        }

        // Only discovery entries are callable, so a caller cannot point this node at an address of its choosing.
        var peer = await forwarding.PeerForAsync(node, http.User, http.RequestAborted).ConfigureAwait(false);

        return peer is null
            ? Results.Problem(
                detail: $"Discovery lists no Messaging node named '{node}'.",
                statusCode: StatusCodes.Status404NotFound
            )
            : await _RelayAsync(http, peer).ConfigureAwait(false);
    }

    private static string? _NodeOf(HttpRequest request)
    {
        var node = request.Query[_NodeParameter].ToString();

        if (node.Length > 0)
        {
            return node;
        }

        // A message's detail names its node in the id, so a link to it works without the parameter.
        return
            request.RouteValues.TryGetValue("kind", out var kind)
            && request.RouteValues.TryGetValue("id", out var id)
            && kind is string kindValue
            && id is string idValue
            ? MessagingForwarding.NodeOf(kindValue, idValue)
            : null;
    }

    private async Task<IResult> _RelayAsync(HttpContext http, MessagingPeer peer)
    {
        var prefix = dashboard.Value.BasePath.TrimEnd('/') + "/api/" + MessagingDashboardOptions.ModuleId;
        var path = http.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase, out var rest)
            ? rest.Value ?? "/"
            : http.Request.Path.Value!;
        var query = QueryString.Create(
            http.Request.Query.Where(static pair => !string.Equals(pair.Key, _NodeParameter, StringComparison.Ordinal))
                .Select(static pair => new KeyValuePair<string, StringValues>(pair.Key, pair.Value))
        );

        var response = await forwarding
            .Client.SendAsync(
                peer,
                HttpMethod.Get,
                path + query.ToUriComponent(),
                http.User,
                operate: false,
                json: null,
                http.RequestAborted
            )
            .ConfigureAwait(false);

        if (!response.Answered)
        {
            http.Response.Headers[ForwardingProtocol.NodeStateHeader] = response.State.ToWire();

            return Results.Problem(
                detail: response.State.Describe(peer.Name),
                statusCode: StatusCodes.Status502BadGateway
            );
        }

        return new RelayedResult(response.Status, response.Body, response.ContentType);
    }

    private sealed class RelayedResult(int status, byte[] body, string? contentType) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = status;

            if (contentType is not null)
            {
                httpContext.Response.ContentType = contentType;
            }

            return httpContext.Response.Body.WriteAsync(body).AsTask();
        }
    }
}
