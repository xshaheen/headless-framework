// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
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
    internal static partial void ConsumerReceivedMessageAfterThreshold(
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
    internal static partial void ExecutedThresholdCallbackFailed(
        this ILogger logger,
        Exception exception,
        string message
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "ConsumerDuplicates",
        Level = LogLevel.Warning,
        Message = "We detected that you have duplicate subscribers ({Subscriber}) in the same subscription ({Subscription}), this will cause diversity behavior."
    )]
    internal static partial void ConsumerDuplicates(this ILogger logger, string subscriber, string? subscription);

    [LoggerMessage(
        EventId = 5,
        EventName = "ConsumerExecutionRetrying",
        Level = LogLevel.Warning,
        Message = "The {Retries}th retrying consume of stored message failed. storage id: {StorageId}"
    )]
    internal static partial void ConsumerExecutionRetrying(this ILogger logger, Guid storageId, int retries);

    [LoggerMessage(
        EventId = 6,
        EventName = "SenderRetrying",
        Level = LogLevel.Warning,
        Message = "The {Retries}th retrying send of stored message failed. storage id: {StorageId} "
    )]
    internal static partial void SenderRetrying(this ILogger logger, Guid storageId, int retries);

    [LoggerMessage(
        EventId = 7,
        EventName = "MessageReceived",
        Level = LogLevel.Debug,
        Message = "Received message. id:{MessageId}, name: {Name}"
    )]
    internal static partial void MessageReceived(this ILogger logger, string messageId, string name);

    [LoggerMessage(
        EventId = 8,
        EventName = "MessagePublishException",
        Level = LogLevel.Error,
        Message = "An exception occurred while publishing a message, reason:{Reason}. message id:{MessageId}"
    )]
    internal static partial void MessagePublishException(
        this ILogger logger,
        Exception? exception,
        string? messageId,
        string reason
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "ConsumerExecuting",
        Level = LogLevel.Information,
        Message = "Executing subscriber method '{ClassName}.{MethodName}' on consumer '{Consumer}'"
    )]
    internal static partial void ConsumerExecuting(
        this ILogger logger,
        string className,
        string methodName,
        string? consumer
    );

    [LoggerMessage(
        EventId = 10,
        EventName = "ConsumerExecuted",
        Level = LogLevel.Information,
        Message = "Executed subscriber method '{ClassName}.{MethodName}' on consumer '{Consumer}' with instance '{Instance}' in {Milliseconds}ms"
    )]
    internal static partial void ConsumerExecuted(
        this ILogger logger,
        string className,
        string methodName,
        string consumer,
        double milliseconds,
        string? instance
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "ConsumerExecuteFailed",
        Level = LogLevel.Error,
        Message = "An exception occurred while executing the subscription method. MessageName:{MessageName}, StorageId:{StorageId}, Instance: {Instance}"
    )]
    internal static partial void ConsumerExecuteFailed(
        this ILogger logger,
        Exception? exception,
        string messageName,
        Guid storageId,
        string? instance
    );

    [LoggerMessage(
        EventId = 12,
        EventName = "ServerStarting",
        Level = LogLevel.Information,
        Message = "Starting the processing server."
    )]
    internal static partial void ServerStarting(this ILogger logger);

    [LoggerMessage(
        EventId = 13,
        EventName = "ProcessorsStartedError",
        Level = LogLevel.Error,
        Message = "Starting the processors throw an exception."
    )]
    internal static partial void ProcessorsStartedError(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 14,
        EventName = "ServerShuttingDown",
        Level = LogLevel.Information,
        Message = "Shutting down the processing server..."
    )]
    internal static partial void ServerShuttingDown(this ILogger logger);

    [LoggerMessage(
        EventId = 15,
        EventName = "ExpectedOperationCanceledException",
        Level = LogLevel.Warning,
        Message = "Expected an OperationCanceledException, but found '{ExMessage}'."
    )]
    internal static partial void ExpectedOperationCanceledException(
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
    internal static partial void MessagingAlreadyStarted(this ILogger logger);

    [LoggerMessage(
        EventId = 17,
        EventName = "MessagingStarting",
        Level = LogLevel.Debug,
        Message = "### Messaging background task is starting."
    )]
    internal static partial void MessagingStarting(this ILogger logger);

    [LoggerMessage(
        EventId = 19,
        EventName = "MessagingStopping",
        Level = LogLevel.Debug,
        Message = "### Messaging background task is stopping."
    )]
    internal static partial void MessagingStopping(this ILogger logger);

    [LoggerMessage(
        EventId = 20,
        EventName = "MessagingStarted",
        Level = LogLevel.Information,
        Message = "### Messaging system started!"
    )]
    internal static partial void MessagingStarted(this ILogger logger);

    [LoggerMessage(
        EventId = 21,
        EventName = "MessagePersistButSystemStopped",
        Level = LogLevel.Warning,
        Message = "The message has been persisted, but the messaging system is currently stopped. It will be attempted to be sent once the system becomes available."
    )]
    internal static partial void MessagePersistButSystemStopped(this ILogger logger);

    [LoggerMessage(
        EventId = 22,
        EventName = "SubscriberInvocationFailed",
        Level = LogLevel.Error,
        Message = "An exception occurred when invoke subscriber. StorageId:{StorageId}"
    )]
    internal static partial void SubscriberInvocationFailed(this ILogger logger, Exception exception, Guid storageId);

    [LoggerMessage(
        EventId = 23,
        EventName = "DelayedStorageUpdateSuccess",
        Level = LogLevel.Debug,
        Message = "Update storage to delayed success of delayed message in memory queue!"
    )]
    internal static partial void DelayedStorageUpdateSuccess(this ILogger logger);

    [LoggerMessage(
        EventId = 24,
        EventName = "DelayedStorageUpdateFailed",
        Level = LogLevel.Warning,
        Message = "Update storage fails of delayed message in memory queue!"
    )]
    internal static partial void DelayedStorageUpdateFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 25,
        EventName = "DelayedMessageSendFailed",
        Level = LogLevel.Error,
        Message = "Delay message sending failed. StorageId: {StorageId} "
    )]
    internal static partial void DelayedMessageSendFailed(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 26,
        EventName = "ScheduledMessageSendError",
        Level = LogLevel.Error,
        Message = "Error sending scheduled message. StorageId: {StorageId}"
    )]
    internal static partial void ScheduledMessageSendError(this ILogger logger, Exception exception, Guid storageId);

    [LoggerMessage(
        EventId = 27,
        EventName = "DelayedMessagePublishFailed",
        Level = LogLevel.Error,
        Message = "Delay message publishing failed unexpectedly, which will stop future scheduled messages from publishing. Restart the application to resume delayed message processing. Exception: {Message}"
    )]
    internal static partial void DelayedMessagePublishFailed(this ILogger logger, Exception exception, string message);

    [LoggerMessage(
        EventId = 28,
        EventName = "TransportSendError",
        Level = LogLevel.Error,
        Message = "An exception occurred when sending a message to the transport. StorageId:{StorageId}"
    )]
    internal static partial void TransportSendError(this ILogger logger, Exception exception, Guid storageId);

    [LoggerMessage(
        EventId = 34,
        EventName = "ConsumerStoredMessageAfterThreshold",
        Level = LogLevel.Warning,
        Message = "The subscriber of stored message {StorageId} still fails after {Retries} executions and will stop retrying."
    )]
    internal static partial void ConsumerStoredMessageAfterThreshold(this ILogger logger, Guid storageId, int retries);

    [LoggerMessage(
        EventId = 35,
        EventName = "SenderStoredMessageAfterThreshold",
        Level = LogLevel.Warning,
        Message = "The publisher of stored message {StorageId} still fails after {Retries} sends and will stop retrying."
    )]
    internal static partial void SenderStoredMessageAfterThreshold(this ILogger logger, Guid storageId, int retries);

    [LoggerMessage(
        EventId = 29,
        EventName = "DisposingWarning",
        Level = LogLevel.Warning,
        Message = "An exception was occurred when disposing."
    )]
    internal static partial void DisposingWarning(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 30,
        EventName = "MessagingShutdown",
        Level = LogLevel.Information,
        Message = "### Messaging system shutdown!"
    )]
    internal static partial void MessagingShutdown(this ILogger logger);

    [LoggerMessage(
        EventId = 31,
        EventName = "TransportChecking",
        Level = LogLevel.Debug,
        Message = "Transport connection checking..."
    )]
    internal static partial void TransportChecking(this ILogger logger);

    [LoggerMessage(
        EventId = 32,
        EventName = "TransportUnhealthy",
        Level = LogLevel.Warning,
        Message = "Transport connection is unhealthy, reconnection..."
    )]
    internal static partial void TransportUnhealthy(this ILogger logger);

    [LoggerMessage(
        EventId = 33,
        EventName = "TransportHealthy",
        Level = LogLevel.Debug,
        Message = "Transport connection healthy!"
    )]
    internal static partial void TransportHealthy(this ILogger logger);

    [LoggerMessage(EventId = 36, Level = LogLevel.Error, Message = "Failed to connect to broker")]
    internal static partial void FailedToConnectToBroker(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 37, Level = LogLevel.Error, Message = "An exception occurred in consumer processing loop")]
    internal static partial void ConsumerProcessingLoopFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 38,
        Level = LogLevel.Warning,
        Message = "Circuit breaker opened; pausing the consumer clients of subscription '{Subscription}'."
    )]
    internal static partial void CircuitBreakerOpenedPausingConsumers(this ILogger logger, string subscription);

    [LoggerMessage(
        EventId = 39,
        Level = LogLevel.Error,
        Message = "Failed to pause consumer client for subscription '{Subscription}'."
    )]
    internal static partial void PauseConsumerClientFailed(
        this ILogger logger,
        Exception exception,
        string subscription
    );

    [LoggerMessage(
        EventId = 40,
        Level = LogLevel.Debug,
        Message = "Resuming the consumer clients of subscription '{Subscription}' (half-open)."
    )]
    internal static partial void ResumingConsumersHalfOpen(this ILogger logger, string subscription);

    [LoggerMessage(
        EventId = 41,
        Level = LogLevel.Error,
        Message = "Failed to resume consumer client for subscription '{Subscription}'."
    )]
    internal static partial void ResumeConsumerClientFailed(
        this ILogger logger,
        Exception exception,
        string subscription
    );

    [LoggerMessage(
        EventId = 4113,
        Level = LogLevel.Debug,
        Message = "Skipping stale circuit intent for subscription '{Subscription}' (intent epoch {Epoch}, last applied {LastAppliedEpoch})."
    )]
    internal static partial void StaleCircuitIntentSkipped(
        this ILogger logger,
        string subscription,
        long epoch,
        long lastAppliedEpoch
    );

    [LoggerMessage(
        EventId = 4114,
        Level = LogLevel.Debug,
        Message = "Delivery admitted while circuit was open during pause latency for consumer '{Consumer}'."
    )]
    internal static partial void DeliveryAdmittedDuringPauseLatency(this ILogger logger, string consumer);

    [LoggerMessage(
        EventId = 4115,
        Level = LogLevel.Warning,
        Message = "Delivery admitted while circuit was open after pause completed for consumer '{Consumer}'; transport is not paused."
    )]
    internal static partial void DeliveryAdmittedWhileOpenAfterPause(this ILogger logger, string consumer);

    [LoggerMessage(
        EventId = 4116,
        EventName = "ReplayedRequestRunsWithoutCaller",
        Level = LogLevel.Information,
        Message = "Request stored as {StorageId} is a replay (generation {Generation}) of a request whose caller no longer waits; it runs as a plain Queue message and sends no reply."
    )]
    internal static partial void ReplayedRequestRunsWithoutCaller(this ILogger logger, Guid storageId, long generation);

    [LoggerMessage(
        EventId = 4129,
        EventName = "MessageLeaseRenewalFailed",
        Level = LogLevel.Warning,
        Message = "Renewing the lease on message {StorageId} failed; the lease it last confirmed is still live, so the renewal is retried on the next tick."
    )]
    internal static partial void MessageLeaseRenewalFailed(this ILogger logger, Exception exception, Guid storageId);

    [LoggerMessage(
        EventId = 4130,
        EventName = "MessageLeaseRenewalFailedPastLease",
        Level = LogLevel.Warning,
        Message = "Renewing the lease on message {StorageId} failed and the lease it last confirmed could lapse before the next tick; the attempt is cancelled."
    )]
    internal static partial void MessageLeaseRenewalFailedPastLease(
        this ILogger logger,
        Exception exception,
        Guid storageId
    );

    [LoggerMessage(
        EventId = 4131,
        EventName = "MessageLeaseLost",
        Level = LogLevel.Warning,
        Message = "The lease on message {StorageId} was lost while its attempt ran; the attempt is cancelled and writes no state, and the row is recovered like a crashed attempt."
    )]
    internal static partial void MessageLeaseLost(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 4125,
        EventName = "StoredMessageDeliverByExceeded",
        Level = LogLevel.Information,
        Message = "Message stored as {StorageId} (name '{MessageName}') passed its delivery deadline {Deadline}; it is marked failed without being sent."
    )]
    internal static partial void StoredMessageDeliverByExceeded(
        this ILogger logger,
        Guid storageId,
        string messageName,
        DateTimeOffset? deadline
    );

    [LoggerMessage(
        EventId = 4126,
        EventName = "MessageDeliverByExceededOnReceive",
        Level = LogLevel.Information,
        Message = "Message {MessageId} (name '{MessageName}', consumer '{Consumer}') arrived after its delivery deadline; it is committed and dropped without running the consumer."
    )]
    internal static partial void MessageDeliverByExceededOnReceive(
        this ILogger logger,
        string? messageId,
        string? messageName,
        string? consumer
    );

    [LoggerMessage(
        EventId = 4127,
        EventName = "MessageDeliverByExceededBeforeAttempt",
        Level = LogLevel.Information,
        Message = "Message stored as {StorageId} passed its delivery deadline before an attempt started; it ends without running the consumer."
    )]
    internal static partial void MessageDeliverByExceededBeforeAttempt(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 4128,
        EventName = "DirectSendDeliverByExceeded",
        Level = LogLevel.Information,
        Message = "Direct send of message {MessageId} (name '{MessageName}') passed its delivery deadline before reaching the transport; it is dropped without being sent."
    )]
    internal static partial void DirectSendDeliverByExceeded(
        this ILogger logger,
        string? messageId,
        string? messageName
    );

    [LoggerMessage(
        EventId = 4132,
        EventName = "MessageDeliverByExceededWhileThrottled",
        Level = LogLevel.Information,
        Message = "Message {MessageId} (name '{MessageName}', consumer '{Consumer}') passed its delivery deadline while waiting for its consumer's rate limit; it is dropped without running the consumer."
    )]
    internal static partial void MessageDeliverByExceededWhileThrottled(
        this ILogger logger,
        string? messageId,
        string? messageName,
        string? consumer
    );

    [LoggerMessage(EventId = 42, Level = LogLevel.Warning, Message = "RabbitMQ consumer cancelled. --> {Reason}")]
    internal static partial void RabbitMqConsumerCancelled(this ILogger logger, string reason);

    [LoggerMessage(EventId = 43, Level = LogLevel.Information, Message = "RabbitMQ consumer registered. --> {Reason}")]
    internal static partial void RabbitMqConsumerRegistered(this ILogger logger, string reason);

    [LoggerMessage(EventId = 44, Level = LogLevel.Warning, Message = "RabbitMQ consumer unregistered. --> {Reason}")]
    internal static partial void RabbitMqConsumerUnregistered(this ILogger logger, string reason);

    [LoggerMessage(EventId = 45, Level = LogLevel.Warning, Message = "RabbitMQ consumer shutdown. --> {Reason}")]
    internal static partial void RabbitMqConsumerShutdown(this ILogger logger, string reason);

    [LoggerMessage(EventId = 46, Level = LogLevel.Error, Message = "Kafka client consume error. --> {Reason}")]
    internal static partial void KafkaClientConsumeError(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 47,
        Level = LogLevel.Warning,
        Message = "Kafka client consume exception, retying... --> {Reason}"
    )]
    internal static partial void KafkaClientConsumeRetrying(this ILogger logger, string reason);

    [LoggerMessage(EventId = 48, Level = LogLevel.Critical, Message = "Kafka server connection error. --> {Reason}")]
    internal static partial void KafkaServerConnectionError(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 49,
        Level = LogLevel.Error,
        Message = "AzureServiceBus subscriber received an error. --> {Reason}"
    )]
    internal static partial void AzureServiceBusSubscriberReceivedError(this ILogger logger, string reason);

    [LoggerMessage(EventId = 50, Level = LogLevel.Error, Message = "NATS subscriber received an error. --> {Reason}")]
    internal static partial void NatsSubscriberReceivedError(this ILogger logger, string reason);

    [LoggerMessage(EventId = 51, Level = LogLevel.Error, Message = "NATS server connection error. --> {Reason}")]
    internal static partial void NatsServerConnectionError(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 52,
        Level = LogLevel.Error,
        Message = "AmazonSQS subscriber delete inflight message failed, invalid id. --> {Reason}"
    )]
    internal static partial void AmazonSqsInvalidIdFormat(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 53,
        Level = LogLevel.Error,
        Message = "AmazonSQS subscriber change message's visibility failed, message isn't in flight. --> {Reason}"
    )]
    internal static partial void AmazonSqsMessageNotInflight(this ILogger logger, string reason);

    [LoggerMessage(EventId = 54, Level = LogLevel.Error, Message = "Redis client consume error. --> {Reason}")]
    internal static partial void RedisClientConsumeError(this ILogger logger, string reason);

    [LoggerMessage(EventId = 55, Level = LogLevel.Warning, Message = "Transport configuration warning. --> {Reason}")]
    internal static partial void TransportConfigurationWarning(this ILogger logger, string reason);

    // Warning, not Error: the broker redelivers an unsettled message and the inbox deduplicates the redelivery, so no
    // message is lost, but repeated failures mean duplicate work and a broker connection worth checking.
    [LoggerMessage(
        EventId = 4120,
        EventName = "TransportSettlementFailed",
        Level = LogLevel.Warning,
        Message = "Transport could not settle a delivery with the broker; the broker will redeliver it. --> {Reason}"
    )]
    internal static partial void TransportSettlementFailed(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 132,
        EventName = "TransportBrokerUnreachable",
        Level = LogLevel.Warning,
        Message = "The transport could not reach the broker and keeps retrying. --> {Reason}"
    )]
    internal static partial void TransportBrokerUnreachable(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 123,
        EventName = "TransportBrokerReachable",
        Level = LogLevel.Information,
        Message = "The transport reached the broker again. --> {Reason}"
    )]
    internal static partial void TransportBrokerReachable(this ILogger logger, string reason);

    [LoggerMessage(
        EventId = 56,
        Level = LogLevel.Error,
        Message = "Message (Name:{GetName},Consumer:{Consumer}) can not be found subscriber. Ensure a consumer with this identity is registered for the message."
    )]
    internal static partial void SubscriberNotFound(this ILogger logger, string? getName, string? consumer);

    [LoggerMessage(
        EventId = 57,
        Level = LogLevel.Information,
        Message = "Stored message {StorageId} execution was canceled by shutdown. Persisting for later retry."
    )]
    internal static partial void StoredMessageExecutionCanceled(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 58,
        Level = LogLevel.Warning,
        Message = "Stored message {StorageId} failed with non-retryable exception: {ExceptionType}. Skipping retries."
    )]
    internal static partial void StoredMessageNonRetryableFailure(
        this ILogger logger,
        Guid storageId,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 63,
        EventName = "TenantIdHeaderRejected",
        Level = LogLevel.Warning,
        Message = "Inbound tenant header was rejected because its length ({Length}) exceeds MessageOptions.TenantIdMaxLength. The consume context will observe a null tenant; investigate the producer if this repeats."
    )]
    internal static partial void TenantIdHeaderRejected(this ILogger logger, int length);

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
    internal static partial void TenantContextSwitched(this ILogger logger, string tenantId);

    [LoggerMessage(
        EventId = 65,
        EventName = "AmbientTenantPropagationDropped",
        Level = LogLevel.Warning,
        Message = "Ambient ICurrentTenant.Id was rejected by tenant propagation middleware because its length ({Length}) exceeds MessageOptions.TenantIdMaxLength or it is whitespace. The publish proceeds without a stamped tenant; investigate the ambient tenant source if this repeats."
    )]
    internal static partial void AmbientTenantPropagationDropped(this ILogger logger, int length);

    [LoggerMessage(
        EventId = 66,
        EventName = "SkippingOnExhaustedAlreadyTerminal",
        Level = LogLevel.Information,
        Message = "Skipping OnExhausted: message {StorageId} already terminal"
    )]
    internal static partial void SkippingOnExhaustedAlreadyTerminal(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 67,
        EventName = "OnExhaustedTimedOut",
        Level = LogLevel.Warning,
        Message = "RetryPolicy.OnExhausted callback for message {StorageId} did not complete within {TimeoutSeconds}s. The callback is orphaned; the dispatch loop has resumed."
    )]
    internal static partial void OnExhaustedTimedOut(this ILogger logger, Guid storageId, double timeoutSeconds);

    [LoggerMessage(
        EventId = 72,
        EventName = "PublishPostSuccessMiddlewareFailed",
        Level = LogLevel.Warning,
        Message = "Publish middleware {MiddlewareType} threw after the inner publish completed; suppressing to avoid duplicate publish."
    )]
    internal static partial void PublishPostSuccessMiddlewareFailed(
        this ILogger logger,
        Exception exception,
        string middlewareType
    );

    [LoggerMessage(
        EventId = 73,
        EventName = "ConsumePostSuccessMiddlewareFailed",
        Level = LogLevel.Warning,
        Message = "Consume middleware {MiddlewareType} threw after the inner consumer completed; suppressing to avoid duplicate consume."
    )]
    internal static partial void ConsumePostSuccessMiddlewareFailed(
        this ILogger logger,
        Exception exception,
        string middlewareType
    );

    [LoggerMessage(
        EventId = 68,
        EventName = "RetryStrategyThrew",
        Level = LogLevel.Error,
        Message = "The Polly retry predicate or delay generator threw {ExceptionType} for message {StorageId}. Treating the retry budget as exhausted."
    )]
    internal static partial void RetryStrategyThrew(
        this ILogger logger,
        Exception ex,
        Guid storageId,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 69,
        EventName = "PublishSucceededButStorageTerminal",
        Level = LogLevel.Information,
        Message = "Publish succeeded after a terminal-Failed write for message {StorageId}; storage state will not be updated. Consumers will observe at-least-once delivery."
    )]
    internal static partial void PublishSucceededButStorageTerminal(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 4121,
        EventName = "PublishSucceededButLeaseLost",
        Level = LogLevel.Warning,
        Message = "Publish succeeded for message {StorageId} but its Succeeded state was not written: the dispatch lease was lost to another claimer or lapsed, or the cause could not be read. The row may be published again; consumers will observe at-least-once delivery. A DispatchTimeout shorter than the publish can cause this."
    )]
    internal static partial void PublishSucceededButLeaseLost(
        this ILogger logger,
        Exception? exception,
        Guid storageId
    );

    [LoggerMessage(
        EventId = 4122,
        EventName = "PublishSucceededButStorageRowMissing",
        Level = LogLevel.Warning,
        Message = "Publish succeeded for message {StorageId} but its Succeeded state was not written: the stored row no longer exists."
    )]
    internal static partial void PublishSucceededButStorageRowMissing(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 70,
        EventName = "SkippingPoisonedOnExhaustedAlreadyTerminal",
        Level = LogLevel.Information,
        Message = "Skipping poisoned-message OnExhausted: message {MessageId} already terminal"
    )]
    internal static partial void SkippingPoisonedOnExhaustedAlreadyTerminal(this ILogger logger, string messageId);

    [LoggerMessage(
        EventId = 71,
        EventName = "SkippingSuccessfulAlreadyTerminal",
        Level = LogLevel.Information,
        Message = "Skipping Succeeded state write: message {StorageId} already terminal"
    )]
    internal static partial void SkippingSuccessfulAlreadyTerminal(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 95,
        EventName = "OnExhaustedCallbackCancelledAtShutdown",
        Level = LogLevel.Debug,
        Message = "RetryPolicy.OnExhausted callback for message {StorageId} was cancelled by host shutdown."
    )]
    internal static partial void OnExhaustedCallbackCancelledAtShutdown(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 96,
        EventName = "OnExhaustedCallbackOrphaned",
        Level = LogLevel.Warning,
        Message = "RetryPolicy.OnExhausted callback for message {StorageId} timed out and is orphaned. Scope-bound services (FailedInfo.ServiceProvider) may become invalid once the dispatch scope disposes; cooperative callbacks should honor the supplied CancellationToken."
    )]
    internal static partial void OnExhaustedCallbackOrphaned(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 74,
        EventName = "RetryStoragePickupFailureEscalated",
        Level = LogLevel.Error,
        Message = "Retry-pickup storage call failed for {ConsecutiveFailures} consecutive cycles. Adaptive polling is backing off; investigate storage health."
    )]
    internal static partial void RetryStoragePickupFailureEscalated(
        this ILogger logger,
        Exception exception,
        int consecutiveFailures
    );

    [LoggerMessage(
        EventId = 75,
        EventName = "DispatcherLoopFaultedAndTerminated",
        Level = LogLevel.Critical,
        Message = "Dispatcher '{LoopName}' loop faulted and terminated. The message pipeline cannot be restarted in place; the host has been signalled to stop so a supervisor (Kubernetes, systemd, IIS) can recycle the process."
    )]
    internal static partial void DispatcherLoopFaultedAndTerminated(
        this ILogger logger,
        Exception exception,
        string loopName
    );

    [LoggerMessage(
        EventId = 76,
        EventName = "DispatcherLoopStopApplicationFailed",
        Level = LogLevel.Error,
        Message = "Dispatcher '{LoopName}' fault path failed to request IHostApplicationLifetime.StopApplication. The original loop fault is preserved; this nested failure is suppressed."
    )]
    internal static partial void DispatcherLoopStopApplicationFailed(
        this ILogger logger,
        Exception exception,
        string loopName
    );

    [LoggerMessage(
        EventId = 77,
        EventName = "UseStorageLockWithNoOpProvider",
        Level = LogLevel.Warning,
        Message = "UseStorageLock is enabled but no real IDistributedLock is registered for messaging (keyed). Coarse-grained mutual exclusion is disabled. Register a real provider via MessagingBuilder.UseDistributedLock(...) (e.g., Headless.DistributedLocks.Redis) or set UseStorageLock = false to silence this warning."
    )]
    internal static partial void UseStorageLockWithNoOpProvider(this ILogger logger);

    [LoggerMessage(
        EventId = 78,
        EventName = "UseStorageLockWithNoOpProviderButRealUnkeyed",
        Level = LogLevel.Warning,
        Message = "UseStorageLock is enabled but no real IDistributedLock is registered under messaging's keyed slot — a real provider is registered un-keyed but messaging requires a keyed registration. Wire it through MessagingBuilder.UseDistributedLock(...) so messaging picks it up under its isolated key."
    )]
    internal static partial void UseStorageLockWithNoOpProviderButRealUnkeyed(this ILogger logger);

    [LoggerMessage(
        EventId = 88,
        EventName = "MessagingRecoveryUsingLockedUntilFloorOnly",
        Level = LogLevel.Information,
        Message = "Messaging Coordination membership is not registered. Dead-incarnation retry recovery falls back to the per-row LockedUntil floor; register a Coordination provider to accelerate orphaned retry rows."
    )]
    internal static partial void MessagingRecoveryUsingLockedUntilFloorOnly(this ILogger logger);

    [LoggerMessage(
        EventId = 94,
        EventName = "MessagingDeadThresholdBelowDispatchTimeout",
        Level = LogLevel.Warning,
        Message = "Coordination DeadThreshold ({DeadThreshold}) is below the retry DispatchTimeout ({DispatchTimeout}). A still-alive node that crosses the dead threshold mid-dispatch will be reclaimed and its message re-dispatched (a redundant at-least-once delivery). Set DeadThreshold >= DispatchTimeout to avoid it."
    )]
    internal static partial void MessagingDeadThresholdBelowDispatchTimeout(
        this ILogger logger,
        TimeSpan deadThreshold,
        TimeSpan dispatchTimeout
    );

    [LoggerMessage(
        EventId = 97,
        EventName = "MessagingDispatchTimeoutMateriallyExceedsInitialGrace",
        Level = LogLevel.Warning,
        Message = "Messaging retry DispatchTimeout ({DispatchTimeout}) exceeds InitialDispatchGrace ({InitialDispatchGrace}) by more than two minutes while ShutdownTimeout is {ShutdownTimeout}. During a crash or when a handler outlives graceful shutdown, retry pickup can remain delayed until LockedUntil expires. Measure the longest valid handler duration before explicitly aligning these values; shortening DispatchTimeout blindly can increase overlapping at-least-once delivery."
    )]
    internal static partial void MessagingDispatchTimeoutMateriallyExceedsInitialGrace(
        this ILogger logger,
        TimeSpan dispatchTimeout,
        TimeSpan initialDispatchGrace,
        TimeSpan shutdownTimeout
    );

    [LoggerMessage(
        EventId = 91,
        EventName = "MessagingDeadOwnerRowsReclaimed",
        Level = LogLevel.Information,
        Message = "{RetryKind} retry recovered {ReclaimedRows} orphaned rows from dead owners."
    )]
    internal static partial void MessagingDeadOwnerRowsReclaimed(
        this ILogger logger,
        string retryKind,
        int reclaimedRows
    );

    [LoggerMessage(
        EventId = 79,
        EventName = "RetryLockLeaseLost",
        Level = LogLevel.Warning,
        Message = "{RetryKind} retry lock lease {LeaseId} for resource {Resource} was observed lost. No new retry pickup starts under this lease; in-flight dispatch remains guarded by per-row LockedUntil."
    )]
    internal static partial void RetryLockLeaseLost(
        this ILogger logger,
        string retryKind,
        string resource,
        string leaseId
    );

    [LoggerMessage(
        EventId = 81,
        EventName = "PublishedRetryLockAcquireFailed",
        Level = LogLevel.Warning,
        Message = "Acquiring the published-retry lock from the lock store failed. Backing off polling and waiting for the next cycle."
    )]
    internal static partial void PublishedRetryLockAcquireFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 82,
        EventName = "PublishedRetryLockAcquireFailureEscalated",
        Level = LogLevel.Error,
        Message = "Acquiring the published-retry lock from the lock store has failed for {ConsecutiveFailures} consecutive cycles. Investigate lock-store health (Redis, SQL, etc.)."
    )]
    internal static partial void PublishedRetryLockAcquireFailureEscalated(
        this ILogger logger,
        Exception ex,
        int consecutiveFailures
    );

    [LoggerMessage(
        EventId = 83,
        EventName = "ReceivedRetryLockAcquireFailed",
        Level = LogLevel.Warning,
        Message = "Acquiring the received-retry lock from the lock store failed. Backing off polling and waiting for the next cycle."
    )]
    internal static partial void ReceivedRetryLockAcquireFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 84,
        EventName = "ReceivedRetryLockAcquireFailureEscalated",
        Level = LogLevel.Error,
        Message = "Acquiring the received-retry lock from the lock store has failed for {ConsecutiveFailures} consecutive cycles. Investigate lock-store health (Redis, SQL, etc.)."
    )]
    internal static partial void ReceivedRetryLockAcquireFailureEscalated(
        this ILogger logger,
        Exception ex,
        int consecutiveFailures
    );

    [LoggerMessage(
        EventId = 85,
        EventName = "StoredMessageMissingLaneTransport",
        Level = LogLevel.Error,
        Message = "Stored published message {StorageId} cannot be dispatched because its lane '{Lane}' has no supported transport. Marked terminal Failed."
    )]
    internal static partial void StoredMessageMissingLaneTransport(
        this ILogger logger,
        Exception exception,
        Guid storageId,
        string lane
    );

    [LoggerMessage(
        EventId = 86,
        EventName = "ConsumeIntentMismatch",
        Level = LogLevel.Warning,
        Message = "Message '{MessageName}' arrived with intent header '{WireIntent}' but consumer is registered for intent '{ConsumerIntent}'. Verify that the producer and consumer use matching intent types."
    )]
    internal static partial void ConsumeIntentMismatch(
        this ILogger logger,
        string messageName,
        string wireIntent,
        string consumerIntent
    );

    [LoggerMessage(
        EventId = 89,
        EventName = "UnsupportedMessageLaneHeader",
        Level = LogLevel.Warning,
        Message = "Message '{MessageName}' arrived with unsupported lane header '{WireLane}' and was rejected before routing."
    )]
    internal static partial void UnsupportedMessageLaneHeader(this ILogger logger, string messageName, string wireLane);

    [LoggerMessage(
        EventId = 87,
        EventName = "ProcessorStopFailed",
        Level = LogLevel.Error,
        Message = "Messaging processor '{ProcessorType}' failed to stop cleanly. Continuing to drain remaining processors."
    )]
    internal static partial void ProcessorStopFailed(this ILogger logger, Exception exception, string processorType);

    [LoggerMessage(
        EventId = 98,
        EventName = "InboxPostCommitDrainFaulted",
        Level = LogLevel.Error,
        Message = "The transactional inbox attempt for message {MessageId} committed durably but draining its unit of work faulted; the attempt is reported as succeeded and the relay recovers any enlisted rows."
    )]
    internal static partial void InboxPostCommitDrainFaulted(this ILogger logger, Exception exception, Guid messageId);

    [LoggerMessage(
        EventId = 99,
        EventName = "ReceiveMessageSkipped",
        Level = LogLevel.Information,
        Message = "Receive middleware {MiddlewareType} skipped message {MessageId} (name '{MessageName}', consumer '{Consumer}', lane '{Lane}'). Reason: {Reason}"
    )]
    internal static partial void ReceiveMessageSkipped(
        this ILogger logger,
        string middlewareType,
        string messageId,
        string messageName,
        string? consumer,
        string lane,
        string? reason
    );

    [LoggerMessage(
        EventId = 100,
        EventName = "ReceiveMessageRejected",
        Level = LogLevel.Warning,
        Message = "Receive middleware {MiddlewareType} rejected message {MessageId} (name '{MessageName}', consumer '{Consumer}', lane '{Lane}'). Reason: {Reason}"
    )]
    internal static partial void ReceiveMessageRejected(
        this ILogger logger,
        Exception? exception,
        string middlewareType,
        string messageId,
        string messageName,
        string? consumer,
        string lane,
        string? reason
    );

    [LoggerMessage(
        EventId = 101,
        EventName = "ReceiveOutcomeCancelled",
        Level = LogLevel.Debug,
        Message = "Receive of message {MessageId} (name '{MessageName}', consumer '{Consumer}') was cancelled before settlement; the transport will redeliver."
    )]
    internal static partial void ReceiveOutcomeCancelled(
        this ILogger logger,
        string messageId,
        string messageName,
        string? consumer
    );

    [LoggerMessage(
        EventId = 102,
        EventName = "ReceivePostSuccessMiddlewareFailed",
        Level = LogLevel.Warning,
        Message = "Receive middleware {MiddlewareType} threw after the inner receive completed; suppressing to avoid discarding the accepted delivery."
    )]
    internal static partial void ReceivePostSuccessMiddlewareFailed(
        this ILogger logger,
        Exception exception,
        string middlewareType
    );

    [LoggerMessage(
        EventId = 103,
        EventName = "EveryInstanceConsumerFailed",
        Level = LogLevel.Error,
        Message = "Every-instance consumer '{Consumer}' failed on message {MessageId} (name '{MessageName}'); the message is dropped because every-instance delivery has no retry."
    )]
    internal static partial void EveryInstanceConsumerFailed(
        this ILogger logger,
        Exception exception,
        string consumer,
        string messageId,
        string messageName
    );

    [LoggerMessage(
        EventId = 104,
        EventName = "EveryInstanceMessageDropped",
        Level = LogLevel.Warning,
        Message = "Every-instance subscription '{Subscription}' dropped message {MessageId} (name '{MessageName}') before its consumer ran: {Reason}"
    )]
    internal static partial void EveryInstanceMessageDropped(
        this ILogger logger,
        Exception? exception,
        string subscription,
        string messageId,
        string messageName,
        string reason
    );

    [LoggerMessage(
        EventId = 105,
        EventName = "SubscriptionEstablishedHookFailed",
        Level = LogLevel.Error,
        Message = "The subscription-established hook of consumer '{Consumer}' failed (generation {Generation}); the subscription keeps receiving."
    )]
    internal static partial void SubscriptionEstablishedHookFailed(
        this ILogger logger,
        Exception exception,
        string consumer,
        long generation
    );

    [LoggerMessage(
        EventId = 106,
        EventName = "OutboxStorageInitFailed",
        Level = LogLevel.Error,
        Message = "Initializing additional outbox {OutboxName} failed. The host starts without it and retries in the background; its relay is paused until then."
    )]
    internal static partial void OutboxStorageInitFailed(this ILogger logger, Exception exception, string outboxName);

    [LoggerMessage(
        EventId = 107,
        EventName = "OutboxStorageInitRetryFailed",
        Level = LogLevel.Warning,
        Message = "Initializing additional outbox {OutboxName} failed again. Retrying in {RetryDelay}."
    )]
    internal static partial void OutboxStorageInitRetryFailed(
        this ILogger logger,
        Exception exception,
        string outboxName,
        TimeSpan retryDelay
    );

    [LoggerMessage(
        EventId = 108,
        EventName = "OutboxStorageInitialized",
        Level = LogLevel.Information,
        Message = "Additional outbox {OutboxName} is initialized; its relay resumes."
    )]
    internal static partial void OutboxStorageInitialized(this ILogger logger, string outboxName);

    [LoggerMessage(
        EventId = 109,
        EventName = "SubscriptionEstablishedHookTimedOut",
        Level = LogLevel.Warning,
        Message = "The subscription-established hook of consumer '{Consumer}' did not finish within {Timeout} (generation {Generation}); its token is canceled and the subscription keeps receiving without waiting for it."
    )]
    internal static partial void SubscriptionEstablishedHookTimedOut(
        this ILogger logger,
        string consumer,
        TimeSpan timeout,
        long generation
    );

    [LoggerMessage(
        EventId = 110,
        EventName = "EveryInstanceDeliveryFaulted",
        Level = LogLevel.Error,
        Message = "Every-instance subscription '{Subscription}' faulted outside its consumer on message {MessageId} (name '{MessageName}'); the message is committed without retry."
    )]
    internal static partial void EveryInstanceDeliveryFaulted(
        this ILogger logger,
        Exception exception,
        string subscription,
        string? messageId,
        string? messageName
    );

    [LoggerMessage(
        EventId = 111,
        EventName = "FailurePolicyRuleThrew",
        Level = LogLevel.Error,
        Message = "A FailWhen rule of the consumer's failure policy threw while classifying {ExceptionType} for message {StorageId}. The failure is treated as matched and the message fails without further retries."
    )]
    internal static partial void FailurePolicyRuleThrew(
        this ILogger logger,
        Exception ex,
        Guid storageId,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 112,
        EventName = "ReplyAddressRefused",
        Level = LogLevel.Warning,
        Message = "The reply to request {RequestId} was not sent: its reply address '{ReplyAddress}' is outside the reserved reply namespace."
    )]
    internal static partial void ReplyAddressRefused(this ILogger logger, string? requestId, string? replyAddress);

    [LoggerMessage(
        EventId = 113,
        EventName = "ReplySendFailed",
        Level = LogLevel.Warning,
        Message = "The reply to request {RequestId} could not be sent and is not retried."
    )]
    internal static partial void ReplySendFailed(this ILogger logger, Exception exception, string? requestId);

    [LoggerMessage(
        EventId = 114,
        EventName = "ReplyDropped",
        Level = LogLevel.Debug,
        Message = "Dropped the reply to request {RequestId}: {DropReason}."
    )]
    internal static partial void ReplyDropped(this ILogger logger, string? requestId, string dropReason);

    [LoggerMessage(
        EventId = 115,
        EventName = "ReplyTenantMismatch",
        Level = LogLevel.Warning,
        Message = "Dropped the reply to request {RequestId}: it carries a different tenant than the request was sent under. The call keeps waiting and times out unless a matching reply arrives."
    )]
    internal static partial void ReplyTenantMismatch(this ILogger logger, string? requestId);

    [LoggerMessage(
        EventId = 116,
        EventName = "ReplyListenerCloseFailed",
        Level = LogLevel.Warning,
        Message = "The reply listener failed to close cleanly during shutdown."
    )]
    internal static partial void ReplyListenerCloseFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 117,
        EventName = "ReplyBuildFailed",
        Level = LogLevel.Warning,
        Message = "The reply to request {RequestId} could not be built, so it is not sent."
    )]
    internal static partial void ReplyBuildFailed(this ILogger logger, Exception exception, string? requestId);

    [LoggerMessage(
        EventId = 118,
        EventName = "ResponderResultDiscarded",
        Level = LogLevel.Debug,
        Message = "Responder '{Consumer}' handled message {MessageId}, which is not a request; its result is discarded."
    )]
    internal static partial void ResponderResultDiscarded(this ILogger logger, string? consumer, string? messageId);

    [LoggerMessage(
        EventId = 119,
        EventName = "RequestExpiredOnReceive",
        Level = LogLevel.Information,
        Message = "Request {MessageId} (name '{MessageName}', consumer '{Consumer}') arrived after its deadline; it is committed and dropped without a reply."
    )]
    internal static partial void RequestExpiredOnReceive(
        this ILogger logger,
        string? messageId,
        string? messageName,
        string? consumer
    );

    [LoggerMessage(
        EventId = 120,
        EventName = "RequestHasNoResponder",
        Level = LogLevel.Warning,
        Message = "Request {MessageId} (name '{MessageName}') reached consumer '{Consumer}', which does not respond to requests; it is committed and dropped with a no_responder fault."
    )]
    internal static partial void RequestHasNoResponder(
        this ILogger logger,
        string? messageId,
        string? messageName,
        string? consumer
    );

    [LoggerMessage(
        EventId = 121,
        EventName = "RequestExpiredBeforeAttempt",
        Level = LogLevel.Information,
        Message = "Request stored as {StorageId} passed its deadline before an attempt started; it ends without running the consumer or replying."
    )]
    internal static partial void RequestExpiredBeforeAttempt(this ILogger logger, Guid storageId);

    [LoggerMessage(
        EventId = 122,
        EventName = "ConsumerSucceededButStateWriteFailed",
        Level = LogLevel.Warning,
        Message = "Consumer succeeded for message {StorageId} but its Succeeded state could not be written; the handler is not run again here and the row is left for the retry pickup once its lease is released or expires."
    )]
    internal static partial void ConsumerSucceededButStateWriteFailed(
        this ILogger logger,
        Exception exception,
        Guid storageId
    );

    [LoggerMessage(
        EventId = 4123,
        EventName = "AdmittedRedeliveryCommitted",
        Level = LogLevel.Information,
        Message = "Message {MessageId} (name '{MessageName}', consumer '{Consumer}', lane '{Lane}') failed before admission, but the inbox already admitted it; the redelivery is committed as a duplicate instead of being stored as poison."
    )]
    internal static partial void AdmittedRedeliveryCommitted(
        this ILogger logger,
        string messageId,
        string messageName,
        string consumer,
        string lane
    );

    [LoggerMessage(
        EventId = 4124,
        EventName = "AdmittedInboxLookupFailed",
        Level = LogLevel.Warning,
        Message = "Could not check whether the inbox already admitted message {MessageId} (consumer '{Consumer}'), which failed before admission; it is stored as poison."
    )]
    internal static partial void AdmittedInboxLookupFailed(
        this ILogger logger,
        Exception exception,
        string messageId,
        string consumer
    );
}
