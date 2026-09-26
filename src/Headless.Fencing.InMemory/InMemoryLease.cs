// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing.InMemory;

/// <summary>One stored lease. Immutable: a change replaces the row, so a reader always holds a consistent snapshot.</summary>
/// <param name="Generation">The generation of the lease's last grant.</param>
/// <param name="State">Whether the attempt is active or how it ended; "expired" is never stored.</param>
/// <param name="GrantedAt">When the last grant happened, by the store's clock.</param>
/// <param name="ExpiresAt">When the active attempt's lease expires, by the store's clock.</param>
/// <param name="EndedAt">When the attempt settled, was released, or was abandoned; <see langword="null" /> while active.</param>
internal sealed record InMemoryLease(
    long Generation,
    InMemoryLeaseState State,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? EndedAt
);

/// <summary>The stored state of a lease.</summary>
internal enum InMemoryLeaseState
{
    Active = 0,
    Settled = 1,
    Released = 2,
    Abandoned = 3,
}
