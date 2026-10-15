// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>Tells a Redis call that could not reach the server from one the server answered with an error.</summary>
internal static class RedisBrokerFailures
{
    /// <summary>
    /// Whether the call lost its connection, timed out, or reached a server that cannot serve yet (loading its data, or
    /// a cluster or primary that is down). An error the server answered about this command, such as a wrong type or a
    /// missing script, returns <see langword="false" />.
    /// </summary>
    public static bool IsUnreachable(Exception exception) =>
        exception
            is RedisConnectionException
                or RedisTimeoutException
                or RedisServerException
                {
                    Kind: RedisErrorKind.ConnectionFault
                        or RedisErrorKind.Timeout
                        or RedisErrorKind.Loading
                        or RedisErrorKind.ClusterDown
                        or RedisErrorKind.MasterDown,
                }
        || BrokerFailures.IsConnectivity(exception);
}
