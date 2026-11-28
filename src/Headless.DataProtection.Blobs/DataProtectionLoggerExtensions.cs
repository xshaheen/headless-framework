// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.DataProtection;

internal static partial class DataProtectionLoggerExtensions
{
    [LoggerMessage(EventId = 1, EventName = "LoadingElements", Level = LogLevel.Trace, Message = "Loading elements...")]
    internal static partial void LogLoadingElements(this ILogger logger);

    [LoggerMessage(
        EventId = 2,
        EventName = "NoElementsFound",
        Level = LogLevel.Trace,
        Message = "No elements were found"
    )]
    internal static partial void LogNoElementsFound(this ILogger logger);

    [LoggerMessage(
        EventId = 3,
        EventName = "FoundElements",
        Level = LogLevel.Trace,
        Message = "Found {FileCount} elements"
    )]
    internal static partial void LogFoundElements(this ILogger logger, int fileCount);

    [LoggerMessage(
        EventId = 4,
        EventName = "LoadingElement",
        Level = LogLevel.Trace,
        Message = "Loading element: {File}"
    )]
    internal static partial void LogLoadingElement(this ILogger logger, string file);

    [LoggerMessage(
        EventId = 5,
        EventName = "FailedToLoadElement",
        Level = LogLevel.Warning,
        Message = "Failed to load element: {File}"
    )]
    internal static partial void LogFailedToLoadElement(this ILogger logger, string file);

    [LoggerMessage(
        EventId = 6,
        EventName = "LoadedElement",
        Level = LogLevel.Trace,
        Message = "Loaded element: {File}"
    )]
    internal static partial void LogLoadedElement(this ILogger logger, string file);

    [LoggerMessage(
        EventId = 7,
        EventName = "SavingElement",
        Level = LogLevel.Trace,
        Message = "Saving element: {File}"
    )]
    internal static partial void LogSavingElement(this ILogger logger, string file);

    [LoggerMessage(EventId = 8, EventName = "SavedElement", Level = LogLevel.Trace, Message = "Saved element: {File}")]
    internal static partial void LogSavedElement(this ILogger logger, string file);

    [LoggerMessage(
        EventId = 9,
        EventName = "MalformedElement",
        Level = LogLevel.Warning,
        Message = "Skipping malformed XML blob: {File}"
    )]
    internal static partial void LogMalformedElement(this ILogger logger, string file, Exception exception);

    [LoggerMessage(
        EventId = 10,
        EventName = "KeyRingValidationSucceeded",
        Level = LogLevel.Information,
        Message = "Data protection key-ring validation succeeded: read {KeyCount} keys from the '{Container}' container"
    )]
    internal static partial void LogKeyRingValidationSucceeded(this ILogger logger, int keyCount, string container);

    [LoggerMessage(
        EventId = 12,
        EventName = "KeyRingValidationFailed",
        Level = LogLevel.Critical,
        Message = "Data protection key-ring validation failed and Check is LogOnly, so readiness is not gated and the validation is not retried. The key ring could not be read from the '{Container}' blob container; the first protected operation will likely fail the same way. Verify the credentials can read the container and that 'resources provision -k DataProtectionKeys' ran for this environment"
    )]
    internal static partial void LogKeyRingValidationFailed(this ILogger logger, Exception exception, string container);

    [LoggerMessage(
        EventId = 15,
        EventName = "KeyRingValidationNotProvisioned",
        Level = LogLevel.Critical,
        Message = "Data protection key-ring validation failed and Check is LogOnly, so readiness is not gated and the validation is not retried: {Problem}. The host never creates the '{Container}' container or the first key; run 'resources provision -k DataProtectionKeys' for this environment"
    )]
    internal static partial void LogKeyRingValidationNotProvisioned(
        this ILogger logger,
        string container,
        string problem
    );
}
