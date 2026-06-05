// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Exception raised by consumers when work should stop because the observed lock handle was lost.</summary>
[PublicAPI]
public sealed class LockHandleLostException : DistributedLockException
{
    /// <summary>Initializes an exception with the default lost-handle message.</summary>
    public LockHandleLostException(string resource, string leaseId)
        : this(
            Argument.IsNotNullOrWhiteSpace(resource),
            Argument.IsNotNullOrWhiteSpace(leaseId),
            $"Distributed lock handle '{leaseId}' for resource '{resource}' was lost."
        ) { }

    /// <summary>Initializes an exception with a custom lost-handle message.</summary>
    public LockHandleLostException(string resource, string leaseId, string? message)
        : base(message)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
        LeaseId = Argument.IsNotNullOrWhiteSpace(leaseId);
    }

    /// <summary>Initializes an exception with a custom lost-handle message and inner cause.</summary>
    public LockHandleLostException(string resource, string leaseId, string? message, Exception? innerException)
        : base(message, innerException)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
        LeaseId = Argument.IsNotNullOrWhiteSpace(leaseId);
    }

    /// <summary>The resource whose lock handle was lost.</summary>
    public string Resource { get; }

    /// <summary>The lock id that was being observed.</summary>
    public string LeaseId { get; }
}
