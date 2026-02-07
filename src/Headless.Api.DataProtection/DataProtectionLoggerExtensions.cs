// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Api;

internal static partial class DataProtectionLoggerExtensions
{
    [LoggerMessage(EventId = 1, EventName = "LoadingElements", Level = LogLevel.Trace, Message = "Loading elements...")]
    public static partial void LogLoadingElements(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "NoElementsFound",
        Level = LogLevel.Trace,
        Message = "No elements were found"
    )]
    public static partial void LogNoElementsFound(this ILogger logger);

    [LoggerMessage(
        EventId = 3,
        EventName = "FoundElements",
        Level = LogLevel.Trace,
        Message = "Found {FileCount} elements"
    )]
    public static partial void LogFoundElements(this ILogger logger, int fileCount);

    [LoggerMessage(
        EventId = 4,
        EventName = "LoadingElement",
        Level = LogLevel.Trace,
        Message = "Loading element: {File}"
    )]
    public static partial void LogLoadingElement(this ILogger logger, string file);

    [LoggerMessage(
        EventId = 5,
        EventName = "FailedToLoadElement",
        Level = LogLevel.Warning,
        Message = "Failed to load element: {File}"
    )]
    public static partial void LogFailedToLoadElement(this ILogger logger, string file);

    [LoggerMessage(
        EventId = 6,
        EventName = "LoadedElement",
        Level = LogLevel.Trace,
        Message = "Loaded element: {File}"
    )]
    public static partial void LogLoadedElement(this ILogger logger, string file);

    [LoggerMessage(
        EventId = 7,
        EventName = "SavingElement",
        Level = LogLevel.Trace,
        Message = "Saving element: {File}"
    )]
    public static partial void LogSavingElement(this ILogger logger, string file);

    [LoggerMessage(EventId = 8, EventName = "SavedElement", Level = LogLevel.Trace, Message = "Saved element: {File}")]
    public static partial void LogSavedElement(this ILogger logger, string file);
}
