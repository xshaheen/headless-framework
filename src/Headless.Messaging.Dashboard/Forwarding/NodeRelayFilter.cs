// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Net.Mime;
using System.Text;
using Headless.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Sends a read of the module's API for another node to that node and returns the node's answer as it is: the node
/// named by <c>?node=</c>, or the node a message id names. A search, a read that is a <c>POST</c>, is relayed with its
/// body. A dashboard-only host sends a read without a node to the
/// first node discovery lists.
/// </summary>
/// <remarks>
/// A node discovery does not list is answered with <c>404</c> before any call leaves this node. A peer that does not
/// answer is answered with <c>502</c>, its state in <c>X-Headless-Node-State</c> and in the problem's detail; this
/// node's own rows never stand in for it. A call another node forwarded is always served here. A path with a segment
/// that could leave the module's API on the peer is answered with <c>400</c> before any call leaves this node, and the
/// peer's answer is always served as JSON.
/// </remarks>
internal sealed class NodeRelayFilter(MessagingForwarding forwarding, IOptions<HeadlessDashboardOptions> dashboard)
    : IEndpointFilter
{
    private const string _NodeParameter = "node";

    // A search's body is its filters and text, a few hundred bytes; the cap keeps a relay from buffering more.
    private const int _MaxRelayedBodyBytes = 16 * 1024;

    /// <summary>The field a refused path names in the <c>400</c> validation problem.</summary>
    internal const string PathField = "path";

    // A backslash becomes a slash in an http URI, and '?' or '#' would end the path, so none of them can sit inside a
    // relayed segment.
    private static readonly SearchValues<char> _RefusedInSegment = SearchValues.Create("\\?#");

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

        if (EscapePath(path) is not { } escaped)
        {
            return DashboardValidation.Problem(
                PathField,
                DashboardValidation.InvalidRule,
                "The path holds a segment a read relayed to another node cannot carry."
            );
        }

        var query = QueryString.Create(
            http.Request.Query.Where(static pair => !string.Equals(pair.Key, _NodeParameter, StringComparison.Ordinal))
                .Select(static pair => new KeyValuePair<string, StringValues>(pair.Key, pair.Value))
        );

        var response = HttpMethods.IsGet(http.Request.Method)
            ? await forwarding
                .Client.RelayAsync(peer, escaped + query.ToUriComponent(), http.User, http.RequestAborted)
                .ConfigureAwait(false)
            : await _RelayBodyAsync(http, peer, escaped + query.ToUriComponent()).ConfigureAwait(false);

        if (response is null)
        {
            return DashboardValidation.Problem(
                DashboardValidation.BodyField,
                DashboardValidation.InvalidRule,
                $"A read relayed to another node carries a body of at most {_MaxRelayedBodyBytes} bytes."
            );
        }

        if (!response.Answered)
        {
            http.Response.Headers[ForwardingProtocol.NodeStateHeader] = response.State.ToWire();

            return Results.Problem(
                detail: response.State.Describe(peer.Name),
                statusCode: StatusCodes.Status502BadGateway
            );
        }

        return new RelayedResult(response.Status, response.Body);
    }

    /// <summary>
    /// Relays a read that carries its arguments in a body, a search, with that body; <see langword="null"/> when the
    /// body is larger than a search's.
    /// </summary>
    private async Task<ForwardResponse?> _RelayBodyAsync(HttpContext http, MessagingPeer peer, string path)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;

        while ((read = await http.Request.Body.ReadAsync(chunk, http.RequestAborted).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _MaxRelayedBodyBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), http.RequestAborted).ConfigureAwait(false);
        }

        return await forwarding
            .Client.RelayAsync(
                peer,
                new HttpMethod(http.Request.Method),
                path,
                buffer.ToArray(),
                http.User,
                http.RequestAborted
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Escapes each segment of <paramref name="path"/> again, or returns <see langword="null"/> when a segment is a dot
    /// segment or holds a character no segment of the module's API can.
    /// </summary>
    /// <remarks>
    /// The server hands the path over decoded, so <c>a%5C..%5C..</c> arrives as <c>a\..\..</c>, which an http URI reads
    /// as two steps up and out of the API. Escaping each decoded segment sends the peer the segments this node routed
    /// on, and a route value such as an id reads the same on both nodes.
    /// </remarks>
    internal static string? EscapePath(string path)
    {
        var builder = new StringBuilder(path.Length + 16);
        var first = true;

        foreach (var segment in path.Split('/'))
        {
            if (
                segment is "." or ".."
                || segment.AsSpan().ContainsAny(_RefusedInSegment)
                || segment.AsSpan().ContainsAnyInRange('\u0000', '\u001F')
                || segment.AsSpan().ContainsAnyInRange('\u007F', '\u009F')
            )
            {
                return null;
            }

            if (!first)
            {
                builder.Append('/');
            }

            builder.Append(Uri.EscapeDataString(segment));
            first = false;
        }

        return builder.ToString();
    }

    private sealed class RelayedResult(int status, byte[] body) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.StatusCode = status;

            // The body is served under the dashboard's origin, so the peer never chooses how the browser reads it: a
            // peer answering text/html would otherwise run its script in the operator's session.
            response.ContentType = status is >= 200 and < 300
                ? MediaTypeNames.Application.Json
                : MediaTypeNames.Application.ProblemJson;
            response.Headers.XContentTypeOptions = "nosniff";

            return response.Body.WriteAsync(body, httpContext.RequestAborted).AsTask();
        }
    }
}
