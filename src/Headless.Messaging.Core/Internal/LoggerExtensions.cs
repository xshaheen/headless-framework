// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ConsumerExecutedAfterThreshold",
        Level = LogLevel.Warning,
        Message = "The Subscriber of the message({MessageId}) still fails after {Retries}th executions and we will stop retrying."
    )]
    public static partial void ConsumerExecutedAfterThreshold(this ILogger logger, string messageId, int retries);

    [LoggerMessage(
        EventId = 2,
        EventName = "SenderAfterThreshold",
        Level = LogLevel.Warning,
        Message = "The Publisher of the message({MessageId}) still fails after {Retries}th sends and we will stop retrying."
    )]
    public static partial void SenderAfterThreshold(this ILogger logger, string messageId, int retries);

    [LoggerMessage(
        EventId = 3,
        EventName = "ExecutedThresholdCallbackFailed",
        Level = LogLevel.Warning,
        Message = "FailedThresholdCallback action raised an exception: {Message}"
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
        Message = "The {Retries}th retrying consume a message failed. message id: {MessageId}"
    )]
    public static partial void ConsumerExecutionRetrying(this ILogger logger, string messageId, int retries);

    [LoggerMessage(
        EventId = 6,
        EventName = "SenderRetrying",
        Level = LogLevel.Warning,
        Message = "The {Retries}th retrying send a message failed. message id: {MessageId} "
    )]
    public static partial void SenderRetrying(this ILogger logger, string messageId, int retries);

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
        Message = "An exception occurred while executing the subscription method. Topic:{Topic}, Id:{Id}, Instance: {Instance}"
    )]
    public static partial void ConsumerExecuteFailed(
        this ILogger logger,
        Exception? exception,
        string topic,
        string id,
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
        Message = "An exception occurred when invoke subscriber. MessageId:{MessageId}"
    )]
    public static partial void SubscriberInvocationFailed(this ILogger logger, Exception exception, string messageId);

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
        Message = "Delay message sending failed. MessageId: {MessageId} "
    )]
    public static partial void DelayedMessageSendFailed(this ILogger logger, string messageId);

    [LoggerMessage(
        EventId = 26,
        EventName = "ScheduledMessageSendError",
        Level = LogLevel.Error,
        Message = "Error sending scheduled message. MessageId: {MessageId}"
    )]
    public static partial void ScheduledMessageSendError(this ILogger logger, Exception exception, string messageId);

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
        Message = "An exception occurred when sending a message to the transport. Id:{MessageId}"
    )]
    public static partial void TransportSendError(this ILogger logger, Exception exception, string messageId);

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
}
