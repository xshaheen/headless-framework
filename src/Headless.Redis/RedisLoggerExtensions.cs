// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Microsoft.Extensions.Logging;

namespace Headless.Redis;

internal static partial class RedisLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "PreparingLuaScript",
        Level = LogLevel.Trace,
        Message = "Preparing Lua scripts"
    )]
    public static partial void LogPreparingLuaScript(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "LoadingLuaScripts",
        Level = LogLevel.Trace,
        Message = "Loading Lua scripts to endpoint {Endpoint}"
    )]
    public static partial void LogLoadingLuaScripts(this ILogger logger, EndPoint endpoint);

    [LoggerMessage(
        EventId = 3,
        EventName = "ScriptsLoadedSuccessfully",
        Level = LogLevel.Trace,
        Message = "Lua scripts loaded successfully in {Elapsed}"
    )]
    public static partial void LogScriptsLoadedSuccessfully(this ILogger logger, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 4,
        EventName = "ConnectionRestored",
        Level = LogLevel.Information,
        Message = "Redis connection restored, scripts will be reloaded"
    )]
    public static partial void LogConnectionRestored(this ILogger logger);
}
