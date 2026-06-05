// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Exception raised when a distributed-lock backend detects deadlock while acquiring a lock.</summary>
[PublicAPI]
public sealed class DistributedLockDeadlockException : DistributedLockException
{
    public DistributedLockDeadlockException(string resource)
        : this(Argument.IsNotNullOrWhiteSpace(resource), $"Distributed lock acquisition for resource '{resource}' deadlocked.") { }

    public DistributedLockDeadlockException(string resource, string? message)
        : base(message)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    public DistributedLockDeadlockException(string resource, string? message, Exception? innerException)
        : base(message, innerException)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    /// <summary>The resource whose lock acquisition deadlocked.</summary>
    public string Resource { get; }
}
