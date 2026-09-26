// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>The outcome of one sweep call.</summary>
/// <param name="Handled">The expired leases whose handler committed; each is now abandoned.</param>
/// <param name="Failures">
/// The expired leases whose handler threw. Each claim rolled back, so the lease is still active and expired, and a
/// later sweep call offers it again.
/// </param>
[PublicAPI]
public sealed record LeaseSweepResult(IReadOnlyList<ExpiredLease> Handled, IReadOnlyList<LeaseSweepFailure> Failures);
