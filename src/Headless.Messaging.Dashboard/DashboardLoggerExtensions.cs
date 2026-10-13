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
}
