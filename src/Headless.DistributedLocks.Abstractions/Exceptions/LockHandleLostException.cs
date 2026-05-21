// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Exception raised by consumers when work should stop because the observed lock handle was lost.</summary>
[PublicAPI]
public sealed class LockHandleLostException : DistributedLockException
{
    public LockHandleLostException(string resource, string lockId)
        : this(
            Argument.IsNotNullOrWhiteSpace(resource),
            Argument.IsNotNullOrWhiteSpace(lockId),
            $"Distributed lock handle '{lockId}' for resource '{resource}' was lost."
        ) { }

    public LockHandleLostException(string resource, string lockId, string? message)
        : base(message)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
        LockId = Argument.IsNotNullOrWhiteSpace(lockId);
    }

    public LockHandleLostException(string resource, string lockId, string? message, Exception? innerException)
        : base(message, innerException)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
        LockId = Argument.IsNotNullOrWhiteSpace(lockId);
    }

    /// <summary>The resource whose lock handle was lost.</summary>
    public string Resource { get; }

    /// <summary>The lock id that was being observed.</summary>
    public string LockId { get; }
}
