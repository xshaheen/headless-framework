// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Retry;
using Headless.Testing.Tests;

namespace Tests.Retry;

public sealed class RetryExceptionClassifierTests : TestBase
{
    public static TheoryData<Exception> PermanentExceptions =>
        new()
        {
            new SubscriberNotFoundException("Not found"),
            new ArgumentNullException("value"),
            new ArgumentException("Invalid", "value"),
            new NotSupportedException("Not supported"),
            new CustomArgumentException(),
        };

    [Theory]
    [MemberData(nameof(PermanentExceptions))]
    public void should_classify_permanent_exceptions(Exception exception)
    {
        RetryExceptionClassifier.IsPermanent(exception).Should().BeTrue();
    }

    [Fact]
    public void should_not_classify_generic_exception_as_permanent()
    {
        RetryExceptionClassifier.IsPermanent(new Exception("Transient")).Should().BeFalse();
    }

    [Fact]
    public void should_not_classify_operation_canceled_exception_as_permanent()
    {
        RetryExceptionClassifier.IsPermanent(new OperationCanceledException()).Should().BeFalse();
    }

    [Fact]
    public void should_not_classify_invalid_operation_exception_as_permanent()
    {
        // InvalidOperationException is commonly thrown for transient conditions (circuit-breaker,
        // pool exhaustion, lock contention) — classifying it as permanent would silently suppress
        // retries and OnExhausted for recoverable failures.
        RetryExceptionClassifier.IsPermanent(new InvalidOperationException("transient")).Should().BeFalse();
    }

    public static TheoryData<Exception> PermanentInnerExceptions =>
        new()
        {
            new SubscriberNotFoundException("Not found"),
            new ArgumentNullException("value"),
            new ArgumentException("Invalid", "value"),
            new NotSupportedException("Not supported"),
        };

    [Theory]
    [MemberData(nameof(PermanentInnerExceptions))]
    public void should_unwrap_subscriber_execution_failed_and_classify_inner_as_permanent(Exception inner)
    {
        // Consumer code throws bare permanent exceptions; SubscribeExecutor wraps them in
        // SubscriberExecutionFailedException before they reach the classifier. The classifier must
        // peel the wrapper so the underlying type is honored — otherwise the framework retries
        // permanent failures indefinitely.
        var wrapped = new SubscriberExecutionFailedException("wrapped", inner);

        RetryExceptionClassifier.IsPermanent(wrapped).Should().BeTrue();
    }

    [Fact]
    public void should_not_classify_subscriber_execution_failed_wrapping_transient_as_permanent()
    {
        var wrapped = new SubscriberExecutionFailedException("wrapped", new TimeoutException("transient"));

        RetryExceptionClassifier.IsPermanent(wrapped).Should().BeFalse();
    }

    private sealed class CustomArgumentException : ArgumentException;
}
