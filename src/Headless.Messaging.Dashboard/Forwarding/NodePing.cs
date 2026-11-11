// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Pings discovered Messaging nodes: one signed call to each node's <c>/peer/ping</c>, a few at a time, each bounded by
/// <see cref="Timeout"/>, answering one result per node.
/// </summary>
/// <remarks>
/// A ping changes nothing, so it is not a command: it needs no confirmation and writes no operator-log entry. It
/// reaches only nodes discovery lists, so a caller can never make this node call an address of its choosing.
/// </remarks>
internal static class NodePing
{
    /// <summary>The most nodes pinged at once.</summary>
    internal const int MaxConcurrency = 8;

    /// <summary>
    /// How long one node gets to answer, across all its discovered instances; the same as a forwarded read waits.
    /// </summary>
    internal static readonly TimeSpan Timeout = ForwardingClient.ReadTimeout;

    /// <summary>Pings each of <paramref name="peers"/> and returns their results in the same order.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<IReadOnlyList<MessagingPingAnswer>> PingAsync(
        ForwardingClient client,
        IReadOnlyList<MessagingPeer> peers,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        Func<Task<MessagingPingAnswer>>[] operations =
        [
            .. peers.Select(peer =>
                (Func<Task<MessagingPingAnswer>>)(() => _PingOneAsync(client, peer, timeProvider, cancellationToken))
            ),
        ];

        return await Task.WhenAllBoundedAsync(operations, MaxConcurrency).ConfigureAwait(false);
    }

    private static async Task<MessagingPingAnswer> _PingOneAsync(
        ForwardingClient client,
        MessagingPeer peer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // A real wait: the node's answer arrives on the wall clock whatever the app clock says.
        timeout.CancelAfter(Timeout);
        var started = timeProvider.GetTimestamp();
        ForwardResponse response;

        try
        {
            response = await client
                .SendAsync(peer, HttpMethod.Get, "/peer/ping", user: null, operate: false, json: null, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return MessagingPingAnswer.Timeout(peer.Name);
        }

        var latency = timeProvider.GetElapsedTime(started);

        if (response.IsSuccess)
        {
            return new MessagingPingAnswer(
                peer.Name,
                MessagingPingAnswer.Answered,
                Math.Round(latency.TotalMilliseconds, 1, MidpointRounding.AwayFromZero),
                State: null,
                Status: null,
                Reason: null
            );
        }

        if (response.TimedOut)
        {
            return MessagingPingAnswer.Timeout(peer.Name);
        }

        // A node that answered without serving the ping, such as one that does not have it yet, is not answering it.
        return response.Answered
            ? new MessagingPingAnswer(
                peer.Name,
                MessagingPingAnswer.Failed,
                LatencyMs: null,
                PeerState.Answering.ToWire(),
                response.Status,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Node {peer.Name} answered the ping with status {response.Status}"
                )
            )
            : new MessagingPingAnswer(
                peer.Name,
                MessagingPingAnswer.Failed,
                LatencyMs: null,
                response.State.ToWire(),
                Status: null,
                response.State.Describe(peer.Name)
            );
    }
}

/// <summary>What one ping found.</summary>
/// <param name="Node">The node pinged.</param>
/// <param name="Outcome"><c>answered</c>, <c>timeout</c>, or <c>failed</c>.</param>
/// <param name="LatencyMs">The round trip in milliseconds, for an answered ping only.</param>
/// <param name="State">For a failed ping, the node's state, such as <c>key-rejected</c> or <c>not-answering</c>.</param>
/// <param name="Status">For a failed ping the node answered, its status code.</param>
/// <param name="Reason">For a failed or timed-out ping, the sentence an operator reads.</param>
internal sealed record MessagingPingAnswer(
    string Node,
    string Outcome,
    double? LatencyMs,
    string? State,
    int? Status,
    string? Reason
)
{
    public const string Answered = "answered";

    public const string TimedOut = "timeout";

    public const string Failed = "failed";

    public static MessagingPingAnswer Timeout(string node) =>
        new(
            node,
            TimedOut,
            LatencyMs: null,
            State: null,
            Status: null,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Node {node} did not answer within {NodePing.Timeout.TotalSeconds:0.#} s"
            )
        );
}

/// <summary>The results of one ping, one per node, in the order discovery lists the nodes.</summary>
internal sealed record MessagingPingResult(IReadOnlyList<MessagingPingAnswer> Items)
{
    /// <summary>Gets what the last discovery read found, or null when forwarding is off.</summary>
    public string? Discovery { get; init; }
}

/// <summary>The body of a ping: the node to ping, or none to ping every node discovery lists.</summary>
internal sealed record MessagingPingRequest(string? Node);

/// <summary>A node's answer to a ping: its name, so the caller sees it reached the node it meant.</summary>
internal sealed record PeerPing(string Node);
