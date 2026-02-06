// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Dashboard;

internal static partial class DashboardLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "CallingRemoteEndpoint",
        Level = LogLevel.Debug,
        Message = "Calling remote endpoint via gateway proxy"
    )]
    public static partial void LogCallingRemoteEndpoint(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "ErrorInvokingDownstreamNode",
        Level = LogLevel.Error,
        Message = "Error invoking downstream node"
    )]
    public static partial void LogErrorInvokingDownstreamNode(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "ConsulGetNodeException",
        Level = LogLevel.Error,
        Message = "Consul get node failed: {Message}"
    )]
    public static partial void LogConsulGetNodeException(this ILogger logger, Exception exception, string message);

    [LoggerMessage(
        EventId = 4,
        EventName = "ConsulGetNodesException",
        Level = LogLevel.Error,
        Message = "Consul get nodes failed: {Message}, inner: {InnerExceptionMessage}"
    )]
    public static partial void LogConsulGetNodesException(
        this ILogger logger,
        string? message,
        string? innerExceptionMessage
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "ConsulNodeRegisterSuccess",
        Level = LogLevel.Information,
        Message = "Consul node registered successfully"
    )]
    public static partial void LogConsulNodeRegisterSuccess(this ILogger logger);
}
