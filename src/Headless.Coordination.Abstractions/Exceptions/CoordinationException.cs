// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Base exception for coordination membership failures.</summary>
[PublicAPI]
public abstract class CoordinationException : Exception
{
    protected CoordinationException() { }

    protected CoordinationException(string? message)
        : base(message) { }

    protected CoordinationException(string? message, Exception? innerException)
        : base(message, innerException) { }
}
