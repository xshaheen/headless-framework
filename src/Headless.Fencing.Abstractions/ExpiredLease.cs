// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// An expired attempt a sweep claimed and marked abandoned, handed to the sweep's handler so the work is routed
/// rather than lost.
/// </summary>
/// <param name="TenantId">The owning tenant, or <see langword="null" /> for the host scope.</param>
/// <param name="Kind">The lease kind.</param>
/// <param name="Resource">The leased resource.</param>
/// <param name="Generation">The abandoned attempt's generation.</param>
/// <param name="ExpiresAt">When the attempt's lease expired, by the database clock.</param>
[PublicAPI]
public sealed record ExpiredLease(
    string? TenantId,
    string Kind,
    string Resource,
    long Generation,
    DateTimeOffset ExpiresAt
);
