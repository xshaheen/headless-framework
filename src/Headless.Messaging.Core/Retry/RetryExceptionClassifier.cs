// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;

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
        var effective = exception is SubscriberExecutionFailedException { InnerException: { } inner }
            ? inner
            : exception;

        return effective
            is SubscriberNotFoundException
                or ArgumentNullException
                or ArgumentException
                or InvalidOperationException
                or NotSupportedException;
    }
}
