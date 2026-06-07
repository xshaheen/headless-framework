// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Controls whether and how a held lease is monitored for liveness.</summary>
[PublicAPI]
public enum LockMonitoringMode
{
    /// <summary>No background monitor. <see cref="IDistributedLease.LostToken"/> is <see cref="CancellationToken.None"/>. Lowest cost; suitable for short-lived locks where lease-loss detection is not needed.</summary>
    None = 0,

    /// <summary>Background monitor validates the lease at the configured polling cadence (default ½ TTL). <see cref="IDistributedLease.LostToken"/> cancels when the lease is detected lost (storage shows a different leaseId) or when accumulated transient failures cross the safety-net threshold. No background renewal — work past TTL fires <c>LostToken</c>.</summary>
    Monitor = 1,

    /// <summary>Background monitor renews the lease at the configured auto-extension cadence (default 1/3 TTL) and validates between renewals. Work past TTL succeeds; <see cref="IDistributedLease.LostToken"/> cancels only on confirmed loss or self-loss after repeated transient failures. Implies <see cref="Monitor"/> behavior plus extension.</summary>
    AutoExtend = 2,
}
