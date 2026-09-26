// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Fencing;

/// <summary>The result of a renewal.</summary>
/// <param name="Status">What the renewal did.</param>
/// <param name="ExpiresAt">
/// The new expiry when <paramref name="Status" /> is <see cref="LeaseRenewalStatus.Renewed" />, the unextended past
/// expiry when it is <see cref="LeaseRenewalStatus.Expired" />, and <see langword="null" /> otherwise. Decided by the
/// database clock.
/// </param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct LeaseRenewalResult(LeaseRenewalStatus Status, DateTimeOffset? ExpiresAt)
{
    /// <summary>Gets whether the lease is still held and its expiry moved forward.</summary>
    public bool IsRenewed => Status == LeaseRenewalStatus.Renewed;
}
