// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Headless.Dashboard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Sends a dashboard call to another Messaging node through the <c>Headless.Messaging.Dashboard.Forwarding</c> client,
/// with the hop marker, the operator's name, and the serving node's view or operate decision.
/// </summary>
/// <remarks>
/// A peer whose address is not https gets no request, unless <see cref="MessagingForwardingOptions.AllowInsecurePeers"/>
/// is set. A failure is never retried against this node's own data: the caller shows the peer's state.
/// </remarks>
internal sealed class ForwardingClient(
    IHttpClientFactory clients,
    PeerDirectory directory,
    MessagingDashboardOptions options,
    IOptions<HeadlessDashboardOptions> dashboard,
    ILogger<ForwardingClient> logger
)
{
    /// <summary>How long a read waits for a peer.</summary>
    internal static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long a command or an Operation chunk waits for a peer.</summary>
    internal static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sends <paramref name="method"/> <paramref name="path"/>, relative to the peer's Messaging API, to
    /// <paramref name="peer"/>.
    /// </summary>
    /// <param name="operate">
    /// Whether this node decided the operator may operate; the peer acts at the lower of this and its own ceiling.
    /// </param>
    public Task<ForwardResponse> SendAsync(
        MessagingPeer peer,
        HttpMethod method,
        string path,
        ClaimsPrincipal? user,
        bool operate,
        byte[]? json,
        CancellationToken cancellationToken
    )
    {
        return _SendAsync(peer, method, path, user, operate, json, relayed: false, cancellationToken);
    }

    /// <summary>
    /// Sends a viewer's read, whose <paramref name="path"/> is escaped and relative to the peer's Messaging API, to
    /// <paramref name="peer"/> at view access. A relayed read never reaches the peer endpoints, which only this node's
    /// own calls use.
    /// </summary>
    public Task<ForwardResponse> RelayAsync(
        MessagingPeer peer,
        string path,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken
    )
    {
        return _SendAsync(
            peer,
            HttpMethod.Get,
            path,
            user,
            operate: false,
            json: null,
            relayed: true,
            cancellationToken
        );
    }

    private async Task<ForwardResponse> _SendAsync(
        MessagingPeer peer,
        HttpMethod method,
        string path,
        ClaimsPrincipal? user,
        bool operate,
        byte[]? json,
        bool relayed,
        CancellationToken cancellationToken
    )
    {
        var forwarding = options.Forwarding;
        var state = PeerState.NotAnswering;

        foreach (var address in peer.Addresses)
        {
            // Refused before the request exists, so neither the key nor the operator's name leaves over plain http.
            if (
                !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                && !forwarding.AllowInsecurePeers
            )
            {
                state = PeerState.InsecurePeer;
                continue;
            }

            var response = await _SendOneAsync(
                    peer,
                    address,
                    method,
                    path,
                    user,
                    operate,
                    json,
                    relayed,
                    cancellationToken
                )
                .ConfigureAwait(false);

            // Another instance of the same node may answer when this one does not.
            if (response.State != PeerState.NotAnswering)
            {
                directory.Report(peer.Name, response.State);

                return response;
            }

            state = PeerState.NotAnswering;
        }

        directory.Report(peer.Name, state);
        logger.PeerNotReached(peer.Name, state);

        return ForwardResponse.Failed(state);
    }

    private async Task<ForwardResponse> _SendOneAsync(
        MessagingPeer peer,
        Uri address,
        HttpMethod method,
        string path,
        ClaimsPrincipal? user,
        bool operate,
        byte[]? json,
        bool relayed,
        CancellationToken cancellationToken
    )
    {
        var api = dashboard.Value.BasePath.TrimEnd('/') + "/api/" + MessagingDashboardOptions.ModuleId;
        var target = new Uri(address, api + path);

        // The key and the operator's name go only to the peer's Messaging API, whatever the path held: the relay
        // escapes a viewer's path, and this check holds if a caller ever passes one unescaped.
        if (!_IsInside(target, address, api, relayed))
        {
            throw new InvalidOperationException("A forwarded call must stay inside the peer's Messaging API.");
        }

        using var request = new HttpRequestMessage(method, target);

        // A fresh body per attempt: sending disposes the request and its content.
        if (json is not null)
        {
            request.Content = new ByteArrayContent(json);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        request.Headers.TryAddWithoutValidation(ForwardingProtocol.HopHeader, options.NodeName);
        request.Headers.TryAddWithoutValidation(
            ForwardingProtocol.AccessHeader,
            operate ? ForwardingProtocol.AccessOperate : ForwardingProtocol.AccessView
        );
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation(CommandEndpoints.ChannelHeader, "ui");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (_ActorOf(user) is { } actor)
        {
            request.Headers.TryAddWithoutValidation(ForwardingProtocol.ActorHeader, Uri.EscapeDataString(actor));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(method == HttpMethod.Get ? ReadTimeout : WriteTimeout);

        try
        {
            using var response = await clients
                .CreateClient(ForwardingProtocol.ClientName)
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);

            // The client caps the buffered answer at Forwarding.MaxResponseBytes, and a larger one fails the send.
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);

            return new ForwardResponse(_StateOf(response), (int)response.StatusCode, body);
        }
        catch (Exception e)
            when ((e is HttpRequestException or OperationCanceledException)
                && !cancellationToken.IsCancellationRequested
            )
        {
            // Only the exception's type is logged: a handler's message could quote a request header.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.PeerCallFailed(peer.Name, e.GetType().Name);
            }

            return ForwardResponse.Failed(PeerState.NotAnswering);
        }
    }

    private static bool _IsInside(Uri target, Uri address, string api, bool relayed)
    {
        // Resolved against the same address, so both sides carry the URI's own escaping and dot-segment removal.
        var root = new Uri(address, api + "/").AbsolutePath;
        var peer = new Uri(address, api + "/peer").AbsolutePath;
        var path = target.AbsolutePath;

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !relayed
            || !(
                string.Equals(path, peer, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(peer + "/", StringComparison.OrdinalIgnoreCase)
            );
    }

    private PeerState _StateOf(HttpResponseMessage response)
    {
        var refusal = response.Headers.TryGetValues(ForwardingProtocol.RefusalHeader, out var values)
            ? values.FirstOrDefault()
            : null;

        return response.StatusCode switch
        {
            // Redirects are not followed, so a peer that answers one has not answered: following it would send the
            // key and the operator's name to wherever the peer points.
            >= HttpStatusCode.Ambiguous and < HttpStatusCode.BadRequest => PeerState.NotAnswering,
            HttpStatusCode.Unauthorized when options.Forwarding.Key is not null => PeerState.KeyRejected,
            HttpStatusCode.Unauthorized => PeerState.PolicyDenied,
            HttpStatusCode.Forbidden
                when string.Equals(refusal, ForwardingProtocol.RefusalPolicy, StringComparison.Ordinal) =>
                PeerState.PolicyDenied,
            HttpStatusCode.Forbidden
                when string.Equals(refusal, ForwardingProtocol.RefusalKey, StringComparison.Ordinal) =>
                PeerState.KeyRejected,
            // The peer admitted this node but caps forwarded calls at view, so a write was refused there.
            HttpStatusCode.Forbidden
                when string.Equals(refusal, ForwardingProtocol.RefusalAccess, StringComparison.Ordinal) =>
                PeerState.ViewOnly,
            _ => PeerState.Answering,
        };
    }

    private static string? _ActorOf(ClaimsPrincipal? user)
    {
        if (user is null)
        {
            return null;
        }

        var authority = DashboardOperatorAuthority.Resolve(user);

        return authority.IsSuccess ? authority.Authorization.Actor : null;
    }
}

internal static partial class ForwardingClientLog
{
    [LoggerMessage(
        EventId = 2151,
        EventName = "MessagingDashboardPeerCallFailed",
        Level = LogLevel.Debug,
        Message = "A forwarded call to Messaging node {Node} got no answer ({Failure})"
    )]
    public static partial void PeerCallFailed(this ILogger logger, string node, string failure);

    [LoggerMessage(
        EventId = 2152,
        EventName = "MessagingDashboardPeerNotReached",
        Level = LogLevel.Information,
        Message = "Messaging node {Node} was not reached: {State}"
    )]
    public static partial void PeerNotReached(this ILogger logger, string node, PeerState state);
}
