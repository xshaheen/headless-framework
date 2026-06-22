// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Exception raised when a distributed-lock backend detects deadlock while acquiring a lock.</summary>
[PublicAPI]
public sealed class DistributedLockDeadlockException : DistributedLockException
{
    /// <summary>Initializes the exception with the default deadlock message for <paramref name="resource"/>.</summary>
    /// <param name="resource">The resource whose lock acquisition deadlocked.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    public DistributedLockDeadlockException(string resource)
        : this(
            Argument.IsNotNullOrWhiteSpace(resource),
            $"Distributed lock acquisition for resource '{resource}' deadlocked."
        ) { }

    /// <summary>Initializes the exception with a custom <paramref name="message"/>.</summary>
    /// <param name="resource">The resource whose lock acquisition deadlocked.</param>
    /// <param name="message">The error message, or <see langword="null"/> for the default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    public DistributedLockDeadlockException(string resource, string? message)
        : base(message)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    /// <summary>Initializes the exception with a custom <paramref name="message"/> and inner cause.</summary>
    /// <param name="resource">The resource whose lock acquisition deadlocked.</param>
    /// <param name="message">The error message, or <see langword="null"/> for the default.</param>
    /// <param name="innerException">The underlying cause, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    public DistributedLockDeadlockException(string resource, string? message, Exception? innerException)
        : base(message, innerException)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    /// <summary>The resource whose lock acquisition deadlocked.</summary>
    public string Resource { get; }
}
