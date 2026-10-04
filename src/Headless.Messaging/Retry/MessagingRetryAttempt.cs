// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Retry;

internal readonly record struct MessagingRetryAttempt(
    OperateResult Result,
    bool CanRetry,
    bool BypassClassification = false
)
{
    public static MessagingRetryAttempt Completed(OperateResult result)
    {
        return new(result, CanRetry: false);
    }

    public static MessagingRetryAttempt Retryable(OperateResult result, bool bypassClassification = false)
    {
        return new(result, CanRetry: true, bypassClassification);
    }
}
