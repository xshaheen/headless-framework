// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging nodes this node forwards to: the entries of service discovery, read when a request first needs them
/// and kept for a few seconds, and what the last forwarded call to each found.
/// </summary>
/// <remarks>
/// <para>
/// Discovery is never called at startup. A request for a node name discovery does not list resolves to nothing, so
/// a <c>?node=</c> value or a stream topic can never send a call to an address the caller chose. This node's own
/// entry is left out, so it never forwards to itself.
/// </para>
/// <para>
/// A discovery entry's name must be the node's <see cref="MessagingDashboardOptions.NodeName"/>: ids name their node
/// by it. Entries with the same name, such as the instances of one Consul service, are one node, tried in order.
/// </para>
/// </remarks>
internal sealed class PeerDirectory(
    IServiceProvider services,
    MessagingDashboardOptions options,
    TimeProvider timeProvider,
    ILogger<PeerDirectory> logger
) : IDisposable
{
    /// <summary>
    /// How long one discovery answer is used before discovery is asked again. A peer that leaves discovery is seen at
    /// most this long plus one change-reader poll later, and its views must go stale within 5 seconds, so it stays
    /// well under 4 seconds.
    /// </summary>
    internal static readonly TimeSpan DiscoveryLifetime = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<string, PeerStatus> _status = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly string? _identityName = services.GetService<CurrentNodeIdentity>()?.Name;
    private IReadOnlyList<MessagingPeer>? _peers;
    private DateTimeOffset _readAt;

    /// <summary>Gets a value indicating whether forwarding is on and a discovery provider is registered.</summary>
    public bool IsEnabled => options.Forwarding.IsEnabled && services.GetService<INodeDiscoveryProvider>() is not null;

    /// <summary>Gets what the last discovery read found, for the "No Messaging node reachable" state.</summary>
    public string DiscoveryState { get; private set; } = "Discovery has not been read yet.";

    /// <summary>Returns the peers discovery lists now, without this node.</summary>
    public async Task<IReadOnlyList<MessagingPeer>> GetPeersAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return [];
        }

        if (_peers is { } cached && timeProvider.GetUtcNow() - _readAt < DiscoveryLifetime)
        {
            return cached;
        }

        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_peers is { } fresh && timeProvider.GetUtcNow() - _readAt < DiscoveryLifetime)
            {
                return fresh;
            }

            _peers = await _ReadAsync(cancellationToken).ConfigureAwait(false);
            _readAt = timeProvider.GetUtcNow();

            // A node that left discovery has no state to report any more.
            foreach (var name in _status.Keys)
            {
                if (!_peers.Any(peer => string.Equals(peer.Name, name, StringComparison.Ordinal)))
                {
                    _status.TryRemove(name, out _);
                }
            }

            return _peers;
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>Returns the peer named <paramref name="node"/>, or <see langword="null"/> when discovery lists none.</summary>
    public async Task<MessagingPeer?> FindAsync(string? node, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(node) || IsLocal(node))
        {
            return null;
        }

        var peers = await GetPeersAsync(cancellationToken).ConfigureAwait(false);

        return peers.FirstOrDefault(peer => string.Equals(peer.Name, node, StringComparison.Ordinal));
    }

    /// <summary>Returns whether <paramref name="node"/> names this node.</summary>
    public bool IsLocal(string node) =>
        string.Equals(node, options.NodeName, StringComparison.Ordinal)
        || (_identityName is not null && string.Equals(node, _identityName, StringComparison.Ordinal));

    /// <summary>Records what a forwarded call to <paramref name="node"/> found.</summary>
    public void Report(string node, PeerState state)
    {
        _status[node] = new PeerStatus(state, timeProvider.GetUtcNow());
    }

    /// <summary>Returns what the last forwarded call to <paramref name="node"/> found, or null before the first.</summary>
    public PeerStatus? StatusOf(string node) => _status.GetValueOrDefault(node);

    public void Dispose() => _refresh.Dispose();

    private async Task<IReadOnlyList<MessagingPeer>> _ReadAsync(CancellationToken cancellationToken)
    {
        var discovery = services.GetRequiredService<INodeDiscoveryProvider>();
        IList<Node> nodes;

        try
        {
            nodes = await discovery.GetNodesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            // Discovery being down must not fail the request that asked for peers: the dashboard names the state.
            logger.DiscoveryFailed(e);
            DiscoveryState = "Service discovery did not answer (" + e.GetType().Name + ").";

            return _peers ?? [];
        }

        var peers = new List<MessagingPeer>();

        foreach (
            var group in nodes
                .Where(node => !IsLocal(node.Name))
                .GroupBy(static node => node.Name, StringComparer.Ordinal)
        )
        {
            var addresses = group.Select(_AddressOf).OfType<Uri>().ToArray();

            if (addresses.Length > 0)
            {
                peers.Add(new MessagingPeer(group.Key, addresses));
            }
        }

        DiscoveryState =
            peers.Count == 0
                ? "Service discovery lists no other Messaging node."
                : $"Service discovery lists {peers.Count} Messaging node(s).";

        return peers;
    }

    private static Uri? _AddressOf(Node node)
    {
        var address =
            node.Port > 0
                ? node.Address.TrimEnd('/')
                    + ":"
                    + node.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : node.Address;

        return
            Uri.TryCreate(address, UriKind.Absolute, out var uri)
            && (
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            )
            ? uri
            : null;
    }
}

internal static partial class PeerDirectoryLog
{
    [LoggerMessage(
        EventId = 2150,
        EventName = "MessagingDashboardDiscoveryFailed",
        Level = LogLevel.Warning,
        Message = "The Messaging dashboard could not read service discovery; peers are shown from the last answer"
    )]
    public static partial void DiscoveryFailed(this ILogger logger, Exception exception);
}
