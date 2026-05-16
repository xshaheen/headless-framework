// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ConsumerReceivedMessageAfterThreshold",
        Level = LogLevel.Warning,
        Message = "The subscriber of received message {MessageId} still fails after {Retries} executions and will stop retrying."
    )]
    public static partial void ConsumerReceivedMessageAfterThreshold(
        this ILogger logger,
        string messageId,
        int retries
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "ExecutedThresholdCallbackFailed",
        Level = LogLevel.Warning,
        Message = "RetryPolicy.OnExhausted action raised an exception: {Message}"
    )]
    public static partial void ExecutedThresholdCallbackFailed(
        this ILogger logger,
        Exception exception,
        string message
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "ConsumerDuplicates",
        Level = LogLevel.Warning,
        Message = "We detected that you have duplicate subscribers ({Subscriber}) in same group ({Group}), this will cause diversity behavior."
    )]
    public static partial void ConsumerDuplicates(this ILogger logger, string subscriber, string? group);

    [LoggerMessage(
        EventId = 5,
        EventName = "ConsumerExecutionRetrying",
        Level = LogLevel.Warning,
        Message = "The {Retries}th retrying consume of stored message failed. storage id: {StorageId}"
    )]
    public static partial void ConsumerExecutionRetrying(this ILogger logger, long storageId, int retries);

    [LoggerMessage(
        EventId = 6,
        EventName = "SenderRetrying",
        Level = LogLevel.Warning,
        Message = "The {Retries}th retrying send of stored message failed. storage id: {StorageId} "
    )]
    public static partial void SenderRetrying(this ILogger logger, long storageId, int retries);

    [LoggerMessage(
        EventId = 7,
        EventName = "MessageReceived",
        Level = LogLevel.Debug,
        Message = "Received message. id:{MessageId}, name: {Name}"
    )]
    public static partial void MessageReceived(this ILogger logger, string messageId, string name);

    [LoggerMessage(
        EventId = 8,
        EventName = "MessagePublishException",
        Level = LogLevel.Error,
        Message = "An exception occurred while publishing a message, reason:{Reason}. message id:{MessageId}"
    )]
    public static partial void MessagePublishException(
        this ILogger logger,
        Exception? exception,
        string? messageId,
        string reason
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "ConsumerExecuting",
        Level = LogLevel.Information,
        Message = "Executing subscriber method '{ClassName}.{MethodName}' on group '{Group}'"
    )]
    public static partial void ConsumerExecuting(
        this ILogger logger,
        string className,
        string methodName,
        string? group
    );

    [LoggerMessage(
        EventId = 10,
        EventName = "ConsumerExecuted",
        Level = LogLevel.Information,
        Message = "Executed subscriber method '{ClassName}.{MethodName}' on group '{Group}' with instance '{Instance}' in {Milliseconds}ms"
    )]
    public static partial void ConsumerExecuted(
        this ILogger logger,
        string className,
        string methodName,
        string group,
        double milliseconds,
        string? instance
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "ConsumerExecuteFailed",
        Level = LogLevel.Error,
        Message = "An exception occurred while executing the subscription method. Topic:{Topic}, StorageId:{StorageId}, Instance: {Instance}"
    )]
    public static partial void ConsumerExecuteFailed(
        this ILogger logger,
        Exception? exception,
        string topic,
        long storageId,
        string? instance
    );

    [LoggerMessage(
        EventId = 12,
        EventName = "ServerStarting",
        Level = LogLevel.Information,
        Message = "Starting the processing server."
    )]
    public static partial void ServerStarting(this ILogger logger);

    [LoggerMessage(
        EventId = 13,
        EventName = "ProcessorsStartedError",
        Level = LogLevel.Error,
        Message = "Starting the processors throw an exception."
    )]
    public static partial void ProcessorsStartedError(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 14,
        EventName = "ServerShuttingDown",
        Level = LogLevel.Information,
        Message = "Shutting down the processing server..."
    )]
    public static partial void ServerShuttingDown(this ILogger logger);

    [LoggerMessage(
        EventId = 15,
        EventName = "ExpectedOperationCanceledException",
        Level = LogLevel.Warning,
        Message = "Expected an OperationCanceledException, but found '{ExMessage}'."
    )]
    public static partial void ExpectedOperationCanceledException(
        this ILogger logger,
        Exception exception,
        string exMessage
    );

    [LoggerMessage(
        EventId = 16,
        EventName = "MessagingAlreadyStarted",
        Level = LogLevel.Information,
        Message = "### Messaging background task is already started!"
    )]
    public static partial void MessagingAlreadyStarted(this ILogger logger);

    [LoggerMessage(
        EventId = 17,
        EventName = "MessagingStarting",
        Level = LogLevel.Debug,
        Message = "### Messaging background task is starting."
    )]
    public static partial void MessagingStarting(this ILogger logger);

    [LoggerMessage(
        EventId = 18,
        EventName = "StorageInitFailed",
        Level = LogLevel.Error,
        Message = "Initializing the storage structure failed!"
    )]
    public static partial void StorageInitFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 19,
        EventName = "MessagingStopping",
        Level = LogLevel.Debug,
        Message = "### Messaging background task is stopping."
    )]
    public static partial void MessagingStopping(this ILogger logger);

    [LoggerMessage(
        EventId = 20,
        EventName = "MessagingStarted",
        Level = LogLevel.Information,
        Message = "### Messaging system started!"
    )]
    public static partial void MessagingStarted(this ILogger logger);

    [LoggerMessage(
        EventId = 21,
        EventName = "MessagePersistButSystemStopped",
        Level = LogLevel.Warning,
        Message = "The message has been persisted, but the messaging system is currently stopped. It will be attempted to be sent once the system becomes available."
    )]
    public static partial void MessagePersistButSystemStopped(this ILogger logger);

    [LoggerMessage(
        EventId = 22,
        EventName = "SubscriberInvocationFailed",
        Level = LogLevel.Error,
        Message = "An exception occurred when invoke subscriber. StorageId:{StorageId}"
    )]
    public static partial void SubscriberInvocationFailed(this ILogger logger, Exception exception, long storageId);

    [LoggerMessage(
        EventId = 23,
        EventName = "DelayedStorageUpdateSuccess",
        Level = LogLevel.Debug,
        Message = "Update storage to delayed success of delayed message in memory queue!"
    )]
    public static partial void DelayedStorageUpdateSuccess(this ILogger logger);

    [LoggerMessage(
        EventId = 24,
        EventName = "DelayedStorageUpdateFailed",
        Level = LogLevel.Warning,
        Message = "Update storage fails of delayed message in memory queue!"
    )]
    public static partial void DelayedStorageUpdateFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 25,
        EventName = "DelayedMessageSendFailed",
        Level = LogLevel.Error,
        Message = "Delay message sending failed. StorageId: {StorageId} "
    )]
    public static partial void DelayedMessageSendFailed(this ILogger logger, long storageId);

    [LoggerMessage(
        EventId = 26,
        EventName = "ScheduledMessageSendError",
        Level = LogLevel.Error,
        Message = "Error sending scheduled message. StorageId: {StorageId}"
    )]
    public static partial void ScheduledMessageSendError(this ILogger logger, Exception exception, long storageId);

    [LoggerMessage(
        EventId = 27,
        EventName = "DelayedMessagePublishFailed",
        Level = LogLevel.Error,
        Message = "Delay message publishing failed unexpectedly, which will stop future scheduled messages from publishing. Restart the application to resume delayed message processing. Exception: {Message}"
    )]
    public static partial void DelayedMessagePublishFailed(this ILogger logger, Exception exception, string message);

    [LoggerMessage(
        EventId = 28,
        EventName = "TransportSendError",
        Level = LogLevel.Error,
        Message = "An exception occurred when sending a message to the transport. StorageId:{StorageId}"
    )]
    public static partial void TransportSendError(this ILogger logger, Exception exception, long storageId);

    [LoggerMessage(
        EventId = 34,
        EventName = "ConsumerStoredMessageAfterThreshold",
        Level = LogLevel.Warning,
        Message = "The subscriber of stored message {StorageId} still fails after {Retries} executions and will stop retrying."
    )]
    public static partial void ConsumerStoredMessageAfterThreshold(this ILogger logger, long storageId, int retries);

    [LoggerMessage(
        EventId = 35,
        EventName = "SenderStoredMessageAfterThreshold",
        Level = LogLevel.Warning,
        Message = "The publisher of stored message {StorageId} still fails after {Retries} sends and will stop retrying."
    )]
    public static partial void SenderStoredMessageAfterThreshold(this ILogger logger, long storageId, int retries);

    [LoggerMessage(
        EventId = 29,
        EventName = "DisposingWarning",
        Level = LogLevel.Warning,
        Message = "An exception was occurred when disposing."
    )]
    public static partial void DisposingWarning(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 30,
        EventName = "MessagingShutdown",
        Level = LogLevel.Information,
        Message = "### Messaging system shutdown!"
    )]
    public static partial void MessagingShutdown(this ILogger logger);

    [LoggerMessage(
        EventId = 31,
        EventName = "TransportChecking",
        Level = LogLevel.Debug,
        Message = "Transport connection checking..."
    )]
    public static partial void TransportChecking(this ILogger logger);

    [LoggerMessage(
        EventId = 32,
        EventName = "TransportUnhealthy",
        Level = LogLevel.Warning,
        Message = "Transport connection is unhealthy, reconnection..."
    )]
    public static partial void TransportUnhealthy(this ILogger logger);

    [LoggerMessage(
        EventId = 33,
        EventName = "TransportHealthy",
        Level = LogLevel.Debug,
        Message = "Transport connection healthy!"
    )]
    public static partial void TransportHealthy(this ILogger logger);

    [LoggerMessage(EventId = 36, Level = LogLevel.Error, Message = "Failed to connect to broker")]
    public static partial void FailedToConnectToBroker(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 37, Level = LogLevel.Error, Message = "An exception occurred in consumer processing loop")]
    public static partial void ConsumerProcessingLoopFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 38,
        Level = LogLevel.Warning,
        Message = "Circuit breaker opened for group '{GroupName}'. Pausing consumers."
    )]
    public static partial void CircuitBreakerOpenedPausingConsumers(this ILogger logger, string groupName);

    [LoggerMessage(
        EventId = 39,
        Level = LogLevel.Error,
        Message = "Failed to pause consumer client for group '{GroupName}'."
    )]
    public static partial void PauseConsumerClientFailed(this ILogger logger, Exception exception, string groupName);

    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Debug,
        Message = "Resuming consumers for group '{GroupName}' (half-open)."
    )]
    public static partial void ResumingConsumersHalfOpen(this ILogger logger, string groupName);

    [LoggerMessage(
        EventId = 41,
        Level = LogLevel.Error,
        Message = "Failed to resume consumer client for group '{GroupName}'."
    )]
    public static partial void ResumeConsumerClientFailed(this ILogger logger, Exception exception, string groupName);

    [LoggerMessage(EventId = 42, Level = LogLevel.Warning, Message = "RabbitMQ consumer cancelled. --> {Reason}")]
    public static partial void RabbitMqConsumerCancelled(this ILogger logger, string reason);

    [LoggerMessage(EventId = 43, Level = LogLevel.Information, Message = "RabbitMQ consumer registered. --> {Reason}")]
    public static partial void RabbitMqConsumerRegistered(this ILogger logger, string reason);

    [LoggerMessage(EventId = 44, Level = LogLevel.Warning, Message = "RabbitMQ consumer unregistered. --> {Reason}")]
    public static partial void RabbitMqConsumerUnregistered(this ILogger logger, string reason);

    [LoggerMessage(EventId = 45, Level = LogLevel.Warning, Message = "RabbitMQ consumer shutdown. --> {Reason}")]
    public static partial void RabbitMqConsumerShutdown(this ILogger logger, string reason);

    [LoggerMessage(EventId = 46, Level = LogLevel.Error, Message = "Kafka client consume error. --> {Reason}")]
    public static partial void KafkaClientConsumeError(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 47,
        Level = LogLevel.Warning,
        Message = "Kafka client consume exception, retying... --> {Reason}"
    )]
    public static partial void KafkaClientConsumeRetrying(this ILogger logger, string reason);

    [LoggerMessage(EventId = 48, Level = LogLevel.Critical, Message = "Kafka server connection error. --> {Reason}")]
    public static partial void KafkaServerConnectionError(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 49,
        Level = LogLevel.Error,
        Message = "AzureServiceBus subscriber received an error. --> {Reason}"
    )]
    public static partial void AzureServiceBusSubscriberReceivedError(this ILogger logger, string reason);

    [LoggerMessage(EventId = 50, Level = LogLevel.Error, Message = "NATS subscriber received an error. --> {Reason}")]
    public static partial void NatsSubscriberReceivedError(this ILogger logger, string reason);

    [LoggerMessage(EventId = 51, Level = LogLevel.Error, Message = "NATS server connection error. --> {Reason}")]
    public static partial void NatsServerConnectionError(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 52,
        Level = LogLevel.Error,
        Message = "AmazonSQS subscriber delete inflight message failed, invalid id. --> {Reason}"
    )]
    public static partial void AmazonSqsInvalidIdFormat(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 53,
        Level = LogLevel.Error,
        Message = "AmazonSQS subscriber change message's visibility failed, message isn't in flight. --> {Reason}"
    )]
    public static partial void AmazonSqsMessageNotInflight(this ILogger logger, string reason);

    [LoggerMessage(EventId = 54, Level = LogLevel.Error, Message = "Redis client consume error. --> {Reason}")]
    public static partial void RedisClientConsumeError(this ILogger logger, string reason);

    [LoggerMessage(EventId = 55, Level = LogLevel.Warning, Message = "Transport configuration warning. --> {Reason}")]
    public static partial void TransportConfigurationWarning(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 56,
        Level = LogLevel.Error,
        Message = "Message (Name:{GetName},Group:{GetGroup}) can not be found subscriber. Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches."
    )]
    public static partial void SubscriberNotFound(this ILogger logger, string? getName, string? getGroup);

    [LoggerMessage(
        EventId = 57,
        Level = LogLevel.Information,
        Message = "Stored message {StorageId} execution was canceled by shutdown. Persisting for later retry."
    )]
    public static partial void StoredMessageExecutionCanceled(this ILogger logger, long storageId);

    [LoggerMessage(
        EventId = 58,
        Level = LogLevel.Warning,
        Message = "Stored message {StorageId} failed with non-retryable exception: {ExceptionType}. Skipping retries."
    )]
    public static partial void StoredMessageNonRetryableFailure(
        this ILogger logger,
        long storageId,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 59,
        EventName = "PublishExecutedFilterFailed",
        Level = LogLevel.Warning,
        Message = "A publish filter threw after the message was accepted. The exception is suppressed to avoid retrying an already-published message. Filter: {FilterType}"
    )]
    public static partial void PublishExecutedFilterFailed(this ILogger logger, Exception exception, string filterType);

    [LoggerMessage(
        EventId = 60,
        EventName = "PublishExceptionFilterFailed",
        Level = LogLevel.Warning,
        Message = "A publish filter threw inside its OnPublishExceptionAsync handler. The original publish exception is preserved; this nested failure is suppressed so the rest of the chain still runs. Filter: {FilterType}"
    )]
    public static partial void PublishExceptionFilterFailed(
        this ILogger logger,
        Exception exception,
        string filterType
    );

    [LoggerMessage(
        EventId = 61,
        EventName = "SubscribeExecutedFilterFailed",
        Level = LogLevel.Warning,
        Message = "A consume filter threw inside its OnSubscribeExecutedAsync handler. The exception is suppressed because the consumer body already committed; surfacing it would trigger a spurious transport retry. Filter: {FilterType}"
    )]
    public static partial void SubscribeExecutedFilterFailed(
        this ILogger logger,
        Exception exception,
        string filterType
    );

    [LoggerMessage(
        EventId = 62,
        EventName = "SubscribeExceptionFilterFailed",
        Level = LogLevel.Warning,
        Message = "A consume filter threw inside its OnSubscribeExceptionAsync handler. The original consumer exception is preserved; this nested failure is suppressed so the rest of the chain still runs. Filter: {FilterType}"
    )]
    public static partial void SubscribeExceptionFilterFailed(
        this ILogger logger,
        Exception exception,
        string filterType
    );

    [LoggerMessage(
        EventId = 63,
        EventName = "TenantIdHeaderRejected",
        Level = LogLevel.Warning,
        Message = "Inbound tenant header was rejected because its length ({Length}) exceeds PublishOptions.TenantIdMaxLength. The consume context will observe a null tenant; investigate the producer if this repeats."
    )]
    public static partial void TenantIdHeaderRejected(this ILogger logger, int length);

    /// <remarks>
    /// This is the only HEADLESS_TENANCY_* event that surfaces the raw tenant identifier. Operators
    /// should gate Debug-level messaging logs accordingly when tenant identifiers contain PII (e.g.,
    /// customer email or organization slugs).
    /// </remarks>
    [LoggerMessage(
        EventId = 64,
        EventName = "TenantContextSwitched",
        Level = LogLevel.Debug,
        Message = "Restoring ICurrentTenant from envelope: switching to tenant '{TenantId}' for the consume scope."
    )]
    public static partial void TenantContextSwitched(this ILogger logger, string tenantId);

    [LoggerMessage(
        EventId = 65,
        EventName = "AmbientTenantPropagationDropped",
        Level = LogLevel.Warning,
        Message = "Ambient ICurrentTenant.Id was rejected by TenantPropagationPublishFilter because its length ({Length}) exceeds PublishOptions.TenantIdMaxLength or it is whitespace. The publish proceeds without a stamped tenant; investigate the ambient tenant source if this repeats."
    )]
    public static partial void AmbientTenantPropagationDropped(this ILogger logger, int length);

    [LoggerMessage(
        EventId = 66,
        EventName = "SkippingOnExhaustedAlreadyTerminal",
        Level = LogLevel.Information,
        Message = "Skipping OnExhausted: message {StorageId} already terminal"
    )]
    public static partial void SkippingOnExhaustedAlreadyTerminal(this ILogger logger, long storageId);

    [LoggerMessage(
        EventId = 67,
        EventName = "OnExhaustedTimedOut",
        Level = LogLevel.Warning,
        Message = "RetryPolicy.OnExhausted callback for message {StorageId} did not complete within {TimeoutSeconds}s. The callback is orphaned; the dispatch loop has resumed."
    )]
    public static partial void OnExhaustedTimedOut(this ILogger logger, long storageId, double timeoutSeconds);

    [LoggerMessage(
        EventId = 68,
        EventName = "BackoffStrategyThrew",
        Level = LogLevel.Error,
        Message = "IRetryBackoffStrategy.Compute threw {ExceptionType} for message {StorageId}. Treating as Exhausted to avoid an infinite retry loop on a buggy strategy."
    )]
    public static partial void BackoffStrategyThrew(
        this ILogger logger,
        Exception ex,
        long storageId,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 69,
        EventName = "BackoffDelayNonFinite",
        Level = LogLevel.Warning,
        Message = "IRetryBackoffStrategy.Compute returned a non-finite delay ({Delay}) for message {StorageId}. Coerced to TimeSpan.Zero."
    )]
    public static partial void BackoffDelayNonFinite(this ILogger logger, long storageId, double delay);
}
