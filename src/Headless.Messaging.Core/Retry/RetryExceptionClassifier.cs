// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;

namespace Headless.Messaging.Retry;

/// <summary>
/// Classifies exceptions by retry behavior for the default retry strategies.
/// </summary>
internal static class RetryExceptionClassifier
{
    /// <summary>
    /// Returns <see langword="true"/> for failures that should not be retried.
    /// </summary>
    /// <remarks>
    /// User code in consumer methods throws bare exceptions (e.g., <see cref="ArgumentException"/>),
    /// but <c>SubscribeExecutor._InvokeConsumerMethodAsync</c> wraps every consumer exception in a
    /// <see cref="SubscriberExecutionFailedException"/> before it reaches the classifier. Unwrapping
    /// the wrapper exactly once mirrors <c>ISubscribeExecutor._PersistFailedStateAsync</c>'s
    /// circuit-breaker reporting path so the classifier observes the same effective exception type.
    /// </remarks>
    public static bool IsPermanent(Exception exception)
    {
        return Unwrap(exception)
            is SubscriberNotFoundException
                or ArgumentNullException
                or ArgumentException
                or NotSupportedException;
    }

    /// <summary>
    /// Returns the handler's own exception when <paramref name="exception"/> is the executor's
    /// <see cref="SubscriberExecutionFailedException"/> wrapper with an inner exception; otherwise
    /// <paramref name="exception"/> itself. Unwraps exactly once.
    /// </summary>
    internal static Exception Unwrap(Exception exception)
    {
        return exception is SubscriberExecutionFailedException { InnerException: { } inner } ? inner : exception;
    }
}
