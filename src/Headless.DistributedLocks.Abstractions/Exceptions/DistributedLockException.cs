// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Base exception for distributed lock failures.</summary>
[PublicAPI]
public abstract class DistributedLockException : Exception
{
    /// <summary>Initializes a new instance with no message.</summary>
    protected DistributedLockException() { }

    /// <summary>Initializes a new instance with the specified <paramref name="message"/>.</summary>
    /// <param name="message">The error message, or <see langword="null"/>.</param>
    protected DistributedLockException(string? message)
        : base(message) { }

    /// <summary>Initializes a new instance with the specified <paramref name="message"/> and inner cause.</summary>
    /// <param name="message">The error message, or <see langword="null"/>.</param>
    /// <param name="innerException">The underlying cause, or <see langword="null"/>.</param>
    protected DistributedLockException(string? message, Exception? innerException)
        : base(message, innerException) { }
}
