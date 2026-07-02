// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisEvents
{
    private readonly IConnectionMultiplexer _connection;
    private readonly ILogger _logger;

    public RedisEvents(IConnectionMultiplexer connection, ILogger logger)
    {
        _logger = logger;
        _connection = connection;
        _connection.ErrorMessage += _Connection_ErrorMessage;
        _connection.ConnectionRestored += _Connection_ConnectionRestored;
        _connection.ConnectionFailed += _Connection_ConnectionFailed;
    }

    private void _Connection_ConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        _logger.LogRedisConnectionFailed(
            e.Exception,
            e.Exception?.Message,
            e.EndPoint,
            e.FailureType,
            e.ConnectionType
        );
    }

    private void _Connection_ConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        _logger.LogRedisConnectionRestored(e.Exception?.Message, e.EndPoint, e.FailureType, e.ConnectionType);
    }

    private void _Connection_ErrorMessage(object? sender, RedisErrorEventArgs e)
    {
        if (e.Message.GetRedisErrorType() == RedisErrorTypes.Unknown)
        {
            _logger.LogRedisServerReplyError(e.Message, e.EndPoint);
        }
    }
}

internal static class RedisConnectionExtensions
{
    public static void LogEvents(this IConnectionMultiplexer connection, ILogger logger)
    {
        Argument.IsNotNull(connection);

        Argument.IsNotNull(logger);

        _ = new RedisEvents(connection, logger);
    }
}

internal static partial class RedisEventsLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "RedisConnectionFailed",
        Level = LogLevel.Error,
        Message = "Connection failed!, {Message}, for endpoint:{Endpoint}, failure type:{FailureType}, connection type:{ConnectionType}"
    )]
    public static partial void LogRedisConnectionFailed(
        this ILogger logger,
        Exception? exception,
        string? message,
        EndPoint? endpoint,
        ConnectionFailureType failureType,
        ConnectionType connectionType
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "RedisConnectionRestored",
        Level = LogLevel.Warning,
        Message = "Connection restored back!, {Message}, for endpoint:{Endpoint}, failure type:{FailureType}, connection type:{ConnectionType}"
    )]
    public static partial void LogRedisConnectionRestored(
        this ILogger logger,
        string? message,
        EndPoint? endpoint,
        ConnectionFailureType failureType,
        ConnectionType connectionType
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "RedisServerReplyError",
        Level = LogLevel.Error,
        Message = "Server replied with error, {Message}, for endpoint:{Endpoint}"
    )]
    public static partial void LogRedisServerReplyError(this ILogger logger, string? message, EndPoint? endpoint);
}
