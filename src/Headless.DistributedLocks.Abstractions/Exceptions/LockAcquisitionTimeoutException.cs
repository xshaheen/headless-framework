// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Exception raised when acquiring a distributed lock exceeds the configured timeout.</summary>
[PublicAPI]
public sealed class LockAcquisitionTimeoutException : DistributedLockException
{
    public LockAcquisitionTimeoutException(string resource)
        : this(resource, $"Unable to acquire distributed lock for resource '{resource}' before the timeout elapsed.")
    { }

    public LockAcquisitionTimeoutException(string resource, string? message)
        : base(message)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    public LockAcquisitionTimeoutException(string resource, string? message, Exception? innerException)
        : base(message, innerException)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    /// <summary>The resource whose lock acquisition timed out.</summary>
    public string Resource { get; }
}
