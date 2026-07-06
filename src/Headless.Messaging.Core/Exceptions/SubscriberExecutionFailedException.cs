// Copyright (c) Mahmoud Shaheen. All rights reserved.

using JetBrains.Annotations;

namespace Headless.Messaging.Exceptions;

/// <summary>
/// Wraps the original exception thrown by a subscriber (<see cref="IConsume{TMessage}"/>) so the retry
/// pipeline can carry the failure while preserving the inner exception's stack trace and source. The
/// retry classifier unwraps this to inspect the real handler exception.
/// </summary>
[PublicAPI]
public sealed class SubscriberExecutionFailedException(string message, Exception ex) : Exception(message, ex)
{
    private readonly Exception _originException = ex;

    public override string? StackTrace => _originException.StackTrace;

    public override string? Source => _originException.Source;
}
