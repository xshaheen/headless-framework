// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// An expired attempt a sweep claimed and marked abandoned, handed to the sweep's handler so the work is routed
/// rather than lost.
/// </summary>
/// <remarks>
/// The abandoned lease keeps its progress and takeover count: the next grant of the lease is
/// <see cref="LeaseGrantStatus.Granted" /> and returns both, so whichever attempt the handler routes the work to can
/// resume it. Settling or releasing that attempt clears them.
/// </remarks>
/// <param name="TenantId">The owning tenant, or <see langword="null" /> for the host scope.</param>
/// <param name="Kind">The lease kind.</param>
/// <param name="Resource">The leased resource.</param>
/// <param name="Generation">The abandoned attempt's generation.</param>
/// <param name="ExpiresAt">When the attempt's lease expired, by the database clock.</param>
/// <param name="TakeoverCount">
/// The lease's takeover count, including this abandonment. See <see cref="LeaseGrantResult.TakeoverCount" />.
/// </param>
/// <param name="Progress">The last progress any attempt recorded, or <see langword="null" /> when none did.</param>
[PublicAPI]
public sealed record ExpiredLease(
    string? TenantId,
    string Kind,
    string Resource,
    long Generation,
    DateTimeOffset ExpiresAt,
    int TakeoverCount = 0,
    LeaseProgress? Progress = null
);
