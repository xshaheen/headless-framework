// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Base exception for distributed lock failures.</summary>
[PublicAPI]
public abstract class DistributedLockException : Exception
{
    protected DistributedLockException() { }

    protected DistributedLockException(string? message)
        : base(message) { }

    protected DistributedLockException(string? message, Exception? innerException)
        : base(message, innerException) { }
}
