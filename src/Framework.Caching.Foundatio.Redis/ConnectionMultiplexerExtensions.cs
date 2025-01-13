// Copyright (c) Mahmoud Shaheen. All rights reserved.

using StackExchange.Redis;

namespace Framework.Caching;

public static class ConnectionMultiplexerExtensions
{
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
