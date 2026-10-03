// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal static class RedisConnectionExtensions
{
    public static void LogEvents(this IConnectionMultiplexer connection, ILogger logger)
    {
        Argument.IsNotNull(connection);

        Argument.IsNotNull(logger);

        _ = new RedisEvents(connection, logger);
    }
}
