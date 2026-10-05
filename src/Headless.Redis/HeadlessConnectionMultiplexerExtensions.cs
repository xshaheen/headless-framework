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
}
