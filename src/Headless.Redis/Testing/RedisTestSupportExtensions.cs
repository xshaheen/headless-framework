// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Headless.Redis.Testing;

/// <summary>
/// Destructive helpers on <see cref="IConnectionMultiplexer"/> intended solely for integration-test
/// setup and teardown. These live in a dedicated <c>Testing</c> namespace so they are not surfaced on
/// the general-purpose Redis extension surface.
/// </summary>
[PublicAPI]
public static class RedisTestSupportExtensions
{
    /// <summary>
    /// Flushes all databases on every writable (non-replica) endpoint of <paramref name="muxer"/>.
    /// </summary>
    /// <param name="muxer">The multiplexer whose endpoints to flush.</param>
    /// <remarks>
    /// This permanently deletes every key on every writable endpoint. Replica endpoints are skipped
    /// automatically. If the multiplexer has no endpoints the method returns immediately without error.
    /// Intended for integration-test teardown only; never call it against production data.
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
}
