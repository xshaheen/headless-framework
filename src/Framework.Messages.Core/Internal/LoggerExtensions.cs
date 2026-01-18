// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Framework.Messages.Internal;

internal static class LoggerExtensions
{
    extension(ILogger logger)
    {
        public void ConsumerExecutedAfterThreshold(string messageId, int retries)
        {
            logger.LogWarning(
                "The Subscriber of the message({MessageId}) still fails after {Retries}th executions and we will stop retrying.",
                messageId,
                retries
            );
        }

        public void SenderAfterThreshold(string messageId, int retries)
        {
            logger.LogWarning(
                "The Publisher of the message({MessageId}) still fails after {Retries}th sends and we will stop retrying.",
                messageId,
                retries
            );
        }

        public void ExecutedThresholdCallbackFailed(Exception ex)
        {
            logger.LogWarning(ex, "FailedThresholdCallback action raised an exception: {Message}", ex.Message);
        }

        public void ConsumerDuplicates(string subscriber, string? group)
        {
            logger.LogWarning(
                "We detected that you have duplicate subscribers ({Subscriber}) in same group ({Group}), this will cause diversity behavior.",
                subscriber,
                group
            );
        }

        public void ConsumerExecutionRetrying(string messageId, int retries)
        {
            logger.LogWarning(
                "The {Retries}th retrying consume a message failed. message id: {MessageId}",
                retries,
                messageId
            );
        }

        public void SenderRetrying(string messageId, int retries)
        {
            logger.LogWarning(
                "The {Retries}th retrying send a message failed. message id: {MessageId} ",
                retries,
                messageId
            );
        }

        public void MessageReceived(string messageId, string name)
        {
            logger.LogDebug("Received message. id:{MessageId}, name: {Name}", messageId, name);
        }

        public void MessagePublishException(string? messageId, string reason, Exception? ex)
        {
            logger.LogError(
                ex,
                "An exception occurred while publishing a message, reason:{Reason}. message id:{MessageId}",
                reason,
                messageId
            );
        }

        public void ConsumerExecuting(string className, string methodName, string? group)
        {
            logger.LogInformation(
                "Executing subscriber method '{ClassName}.{MethodName}' on group '{Group}'",
                className,
                methodName,
                group
            );
        }

        public void ConsumerExecuted(
            string className,
            string methodName,
            string group,
            double milliseconds,
            string? instance
        )
        {
            logger.LogInformation(
                "Executed subscriber method '{ClassName}.{MethodName}' on group '{Group}' with instance '{Instance}' in {Milliseconds}ms",
                className,
                methodName,
                group,
                instance,
                milliseconds
            );
        }

        public void ConsumerExecuteFailed(string topic, string id, string? instance, Exception? ex)
        {
            logger.LogError(
                ex,
                "An exception occurred while executing the subscription method. Topic:{Topic}, Id:{Id}, Instance: {Instance}",
                topic,
                id,
                instance
            );
        }

        public void ServerStarting()
        {
            logger.LogInformation("Starting the processing server.");
        }

        public void ProcessorsStartedError(Exception ex)
        {
            logger.LogError(ex, "Starting the processors throw an exception.");
        }

        public void ServerShuttingDown()
        {
            logger.LogInformation("Shutting down the processing server...");
        }

        public void ExpectedOperationCanceledException(Exception ex)
        {
            logger.LogWarning(ex, "Expected an OperationCanceledException, but found '{ExMessage}'.", ex.Message);
        }
    }
}
