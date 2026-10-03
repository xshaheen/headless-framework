// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

internal static partial class RetryProcessorLog
{
    [LoggerMessage(
        EventId = 3107,
        Level = LogLevel.Error,
        Message = "Unhandled exception in published-message retry processing"
    )]
    public static partial void PublishedRetryProcessingUnhandled(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 3108,
        Level = LogLevel.Error,
        Message = "Unhandled exception in received-message retry processing"
    )]
    public static partial void ReceivedRetryProcessingUnhandled(this ILogger logger, Exception? ex);

    [LoggerMessage(
        EventId = 3109,
        Level = LogLevel.Debug,
        Message = "Skipping retry for message {StorageId} — circuit open for consumer {Consumer}"
    )]
    public static partial void RetrySkippedBecauseCircuitOpen(this ILogger logger, Guid storageId, string? consumer);

    [LoggerMessage(
        EventId = 3119,
        Level = LogLevel.Warning,
        Message = "Circuit retry disposition failed for message {StorageId} for consumer {Consumer}; retaining the claimed lease"
    )]
    public static partial void CircuitRetryDispositionFailed(
        this ILogger logger,
        Exception exception,
        Guid storageId,
        string? consumer
    );

    [LoggerMessage(
        EventId = 3120,
        Level = LogLevel.Warning,
        Message = "Storage provider {Provider} does not support atomic circuit retry deferral; retaining circuit-open leases until expiry"
    )]
    public static partial void CircuitRetryDeferralUnsupported(this ILogger logger, string provider);

    [LoggerMessage(
        EventId = 3121,
        Level = LogLevel.Warning,
        Message = "Circuit retry deferral was rejected by the store fence for message {StorageId} for consumer {Consumer} (stale generation, lapsed lease, or terminal row); the claim is retained until its lease expires"
    )]
    public static partial void CircuitRetryDeferralRejected(this ILogger logger, Guid storageId, string? consumer);

    [LoggerMessage(
        EventId = 3122,
        Level = LogLevel.Warning,
        Message = "No ICircuitBreakerStateManager is registered; circuit-open retry claims (first: message {StorageId} for consumer {Consumer}) are retained until lease expiry instead of being deferred or probed"
    )]
    public static partial void CircuitRetryRetainedWithoutStateManager(
        this ILogger logger,
        Guid storageId,
        string? consumer
    );

    [LoggerMessage(EventId = 3110, Level = LogLevel.Warning, Message = "Get messages from storage failed. Retrying...")]
    public static partial void GetMessagesFromStorageFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 3111,
        Level = LogLevel.Debug,
        Message = "Adaptive polling: circuit-open rate exceeds threshold, interval increased to {Interval}"
    )]
    public static partial void AdaptivePollingIntervalIncreased(this ILogger logger, TimeSpan interval);

    [LoggerMessage(
        EventId = 3112,
        Level = LogLevel.Debug,
        Message = "Adaptive polling: healthy for 2 cycles, interval decreased to {Interval}"
    )]
    public static partial void AdaptivePollingIntervalDecreased(this ILogger logger, TimeSpan interval);
}
