// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
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
            new InvalidOperationException("Invalid operation"),
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

    private sealed class CustomArgumentException : ArgumentException;
}
