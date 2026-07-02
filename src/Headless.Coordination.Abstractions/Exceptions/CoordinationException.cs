// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Coordination;

/// <summary>Base exception for coordination membership failures.</summary>
[PublicAPI]
public abstract class CoordinationException : Exception
{
    /// <inheritdoc cref="Exception()"/>
    protected CoordinationException() { }

    /// <inheritdoc cref="Exception(string)"/>
    protected CoordinationException(string? message)
        : base(message) { }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    protected CoordinationException(string? message, Exception? innerException)
        : base(message, innerException) { }
}
