// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Idempotency;

/// <summary>The result of renewing an admitted attempt's lease.</summary>
/// <param name="Status">
/// <see cref="IdempotentLeaseStatus.Current" /> when the lease was extended; otherwise why the attempt no longer owns
/// the key, and nothing was written.
/// </param>
/// <param name="ExpiresAt">
/// The new expiry when <paramref name="Status" /> is <see cref="IdempotentLeaseStatus.Current" />, the unextended past
/// expiry when it is <see cref="IdempotentLeaseStatus.Expired" />, and <see langword="null" /> otherwise. Decided by the
/// database clock.
/// </param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct IdempotentLeaseRenewal(IdempotentLeaseStatus Status, DateTimeOffset? ExpiresAt)
{
    /// <summary>Gets whether the attempt still owns the key and its lease expiry moved forward.</summary>
    public bool IsRenewed => Status == IdempotentLeaseStatus.Current;
}
