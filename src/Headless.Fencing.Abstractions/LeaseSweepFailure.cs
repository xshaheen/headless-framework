// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>An expired lease whose sweep handler threw.</summary>
/// <param name="Lease">The lease the handler was given.</param>
/// <param name="Exception">What the handler threw.</param>
[PublicAPI]
public sealed record LeaseSweepFailure(ExpiredLease Lease, Exception Exception);
