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
    public async Task<ForwardResponse> SendAsync(
        MessagingPeer peer,
        HttpMethod method,
        string path,
        ClaimsPrincipal? user,
        bool operate,
        byte[]? json,
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

            var response = await _SendOneAsync(peer, address, method, path, user, operate, json, cancellationToken)
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
        CancellationToken cancellationToken
    )
    {
        var basePath = dashboard.Value.BasePath.TrimEnd('/');
        var target = new Uri(address, basePath + "/api/" + MessagingDashboardOptions.ModuleId + path);

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

            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString();

            return new ForwardResponse(_StateOf(response), (int)response.StatusCode, body, contentType);
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

    private PeerState _StateOf(HttpResponseMessage response)
    {
        var refusal = response.Headers.TryGetValues(ForwardingProtocol.RefusalHeader, out var values)
            ? values.FirstOrDefault()
            : null;

        return response.StatusCode switch
        {
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
