// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

public sealed class SubscriberExecutionFailedException(string message, Exception ex) : Exception(message, ex)
{
    private readonly Exception _originException = ex;

    public override string? StackTrace => _originException.StackTrace;

    public override string? Source => _originException.Source;
}
