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
    public static bool IsPermanent(Exception exception) =>
        exception
            is SubscriberNotFoundException
                or ArgumentNullException
                or ArgumentException
                or InvalidOperationException
                or NotSupportedException;
}
