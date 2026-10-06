// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using StackExchange.Redis;

namespace Headless.Redis;

/// <summary>Utility extensions for <see cref="IConnectionMultiplexer"/>.</summary>
[PublicAPI]
public static class HeadlessConnectionMultiplexerExtensions
{
    /// <summary>
    /// Returns the total number of keys stored across all writable (non-replica) endpoints.
    /// </summary>
    /// <param name="muxer">The multiplexer whose endpoints to query.</param>
    /// <param name="cancellationToken">
    /// Token checked before endpoint discovery and between endpoint queries. StackExchange.Redis does not expose
    /// cancellation for <c>DBSIZE</c>, so an in-flight endpoint query cannot be interrupted.
    /// </param>
    /// <returns>
    /// The sum of key counts from all primary endpoints, or <c>0</c> when the multiplexer has no
    /// endpoints. Each endpoint is queried via <c>DBSIZE</c> on its default database.
    /// </returns>
    public static async Task<long> CountAllKeysAsync(
        this IConnectionMultiplexer muxer,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoints = muxer.GetEndPoints();

        if (endpoints.Length == 0)
        {
            return 0;
        }

        long count = 0;

        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = muxer.GetServer(endpoint);

            if (!server.IsReplica)
            {
                count += await server.DatabaseSizeAsync();
            }
        }

        return count;
    }

    /// <summary>Sends <c>PING</c> through the default database and returns the round-trip time.</summary>
    /// <param name="muxer">The multiplexer to probe.</param>
    /// <param name="cancellationToken">
    /// Token that abandons the wait. StackExchange.Redis does not expose cancellation for <c>PING</c>, so the command
    /// itself still ends through the multiplexer's own timeout.
    /// </param>
    /// <returns>The time the server took to answer.</returns>
    /// <exception cref="RedisConnectionException">No connection to the server is available.</exception>
    /// <exception cref="RedisTimeoutException">The server did not answer within the multiplexer's timeout.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static Task<TimeSpan> PingAsync(
        this IConnectionMultiplexer muxer,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(muxer);

        return muxer.GetDatabase().PingAsync().WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Probes every endpoint the multiplexer was configured with, the way a readiness check should: a <c>PING</c> to
    /// each standalone, replica, or sentinel server, and <c>CLUSTER INFO</c> on each cluster node, which must report
    /// <c>cluster_state:ok</c>.
    /// </summary>
    /// <param name="muxer">The multiplexer to probe.</param>
    /// <param name="cancellationToken">
    /// Token that abandons the wait. StackExchange.Redis does not expose cancellation for these commands, so an
    /// in-flight command still ends through the multiplexer's own timeout.
    /// </param>
    /// <returns>A task that completes when every configured endpoint answered.</returns>
    /// <remarks>
    /// A single <c>PING</c> through the default database proves only that one server answers. A multi-endpoint
    /// configuration can lose a node, and a cluster can answer pings while its slots are not all served.
    /// </remarks>
    /// <exception cref="RedisConnectionException">No connection to an endpoint is available.</exception>
    /// <exception cref="RedisTimeoutException">An endpoint did not answer within the multiplexer's timeout.</exception>
    /// <exception cref="InvalidOperationException">A cluster node does not report <c>cluster_state:ok</c>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static async Task ProbeEndpointsAsync(
        this IConnectionMultiplexer muxer,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(muxer);

        foreach (var endPoint in muxer.GetEndPoints(configuredOnly: true))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var server = muxer.GetServer(endPoint);

            if (server.ServerType != ServerType.Cluster)
            {
                await server.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

                continue;
            }

            var clusterInfo = await server
                .ExecuteAsync("CLUSTER", "INFO")
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            // The endpoint stays out of the message: the failure text can reach a health report.
            if (
                clusterInfo.IsNull
                || clusterInfo.ToString()?.Contains("cluster_state:ok", StringComparison.Ordinal) != true
            )
            {
                throw new InvalidOperationException("A Redis cluster node does not report cluster_state:ok.");
            }
        }
    }
}
