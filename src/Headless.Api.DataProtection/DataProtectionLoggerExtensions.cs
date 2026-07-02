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

    [LoggerMessage(
        EventId = 9,
        EventName = "MalformedElement",
        Level = LogLevel.Warning,
        Message = "Skipping malformed XML blob: {File}"
    )]
    public static partial void LogMalformedElement(this ILogger logger, string file, Exception exception);

    [LoggerMessage(
        EventId = 10,
        EventName = "StartupValidationRoundTripSucceeded",
        Level = LogLevel.Information,
        Message = "Data protection startup validation succeeded: a protect/unprotect round-trip exercised the key ring against the '{Container}' container"
    )]
    public static partial void LogStartupValidationRoundTripSucceeded(this ILogger logger, string container);

    [LoggerMessage(
        EventId = 11,
        EventName = "StartupValidationReadProbeSucceeded",
        Level = LogLevel.Information,
        Message = "Data protection startup validation succeeded: the read-only key-ring probe loaded {KeyCount} keys from the '{Container}' container (AutoGenerateKeys is false, so no key was generated)"
    )]
    public static partial void LogStartupValidationReadProbeSucceeded(
        this ILogger logger,
        int keyCount,
        string container
    );

    [LoggerMessage(
        EventId = 12,
        EventName = "StartupValidationFailed",
        Level = LogLevel.Critical,
        Message = "Data protection startup validation failed and Mode is LogOnly — continuing startup. The key ring could not be exercised against the '{Container}' blob container; the first key write or the ~90-day key rotation will likely fail the same way. Verify the container exists and the credentials allow access, wire an IBlobContainerManager so PersistKeysToBlobStorage ensures the container before writes, or provision the container out-of-band (BlobContainerProvisioning.PreProvisioned)"
    )]
    public static partial void LogStartupValidationFailed(this ILogger logger, Exception exception, string container);

    [LoggerMessage(
        EventId = 13,
        EventName = "StartupValidationWriteProbeSucceeded",
        Level = LogLevel.Information,
        Message = "Data protection startup validation write probe succeeded: uploaded and deleted the reserved sentinel blob in the '{Container}' container through the key-write pipeline"
    )]
    public static partial void LogStartupValidationWriteProbeSucceeded(this ILogger logger, string container);

    [LoggerMessage(
        EventId = 14,
        EventName = "StartupValidationWriteProbeSkipped",
        Level = LogLevel.Debug,
        Message = "Data protection startup validation write probe skipped: the configured IXmlRepository ('{RepositoryType}') is not the blob-storage repository, so there is no generic write to safely perform"
    )]
    public static partial void LogStartupValidationWriteProbeSkipped(this ILogger logger, string repositoryType);

    [LoggerMessage(
        EventId = 15,
        EventName = "StartupValidationEmptyKeyRing",
        Level = LogLevel.Critical,
        Message = "Data protection startup validation failed and Mode is LogOnly — continuing startup. The key-ring read probe reached the '{Container}' container but found no keys; AutoGenerateKeys is false on this node so it cannot create one, and its first protected operation will fail. Has the designated key writer run yet? Is this the right container/storage for this environment?"
    )]
    public static partial void LogStartupValidationEmptyKeyRing(this ILogger logger, string container);
}
