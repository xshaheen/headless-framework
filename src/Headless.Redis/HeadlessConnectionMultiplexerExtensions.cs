// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
}
