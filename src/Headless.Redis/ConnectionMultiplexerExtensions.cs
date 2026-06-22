// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Redis;

/// <summary>Utility extensions for <see cref="IConnectionMultiplexer"/>.</summary>
[PublicAPI]
public static class ConnectionMultiplexerExtensions
{
    /// <summary>
    /// Flushes all databases on every writable (non-replica) endpoint of <paramref name="muxer"/>.
    /// </summary>
    /// <param name="muxer">The multiplexer whose endpoints to flush.</param>
    /// <remarks>
    /// Replica endpoints are skipped automatically. If the multiplexer has no endpoints the method
    /// returns immediately without error. Intended for integration-test teardown; do not call against
    /// production data.
    /// </remarks>
    public static async Task FlushAllAsync(this IConnectionMultiplexer muxer)
    {
        var endpoints = muxer.GetEndPoints();

        if (endpoints.Length == 0)
        {
            return;
        }

        foreach (var endpoint in endpoints)
        {
            var server = muxer.GetServer(endpoint);

            if (!server.IsReplica)
            {
                await server.FlushAllDatabasesAsync();
            }
        }
    }

    /// <summary>
    /// Returns the total number of keys stored across all writable (non-replica) endpoints.
    /// </summary>
    /// <param name="muxer">The multiplexer whose endpoints to query.</param>
    /// <returns>
    /// The sum of key counts from all primary endpoints, or <c>0</c> when the multiplexer has no
    /// endpoints. Each endpoint is queried via <c>DBSIZE</c> on its default database.
    /// </returns>
    public static async Task<long> CountAllKeysAsync(this IConnectionMultiplexer muxer)
    {
        var endpoints = muxer.GetEndPoints();

        if (endpoints.Length == 0)
        {
            return 0;
        }

        long count = 0;

        foreach (var endpoint in endpoints)
        {
            var server = muxer.GetServer(endpoint);

            if (!server.IsReplica)
            {
                count += await server.DatabaseSizeAsync();
            }
        }

        return count;
    }
}
