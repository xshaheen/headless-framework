// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;

namespace Headless.Jobs.Models;

/// <summary>
/// The earliest pending second of time jobs, together with the instant the STORE observed while reading them.
/// </summary>
/// <remarks>
/// <see cref="StoreUtcNow"/> rides along on the same statement as the peek — no extra round trip — for the same reason
/// <see cref="CronDispatchCandidates.StoreUtcNow"/> does: due-ness in this subsystem is arbitrated by the store's clock
/// (the claim predicates compare against the database clock), so a wake computed against the calling node's clock
/// oversleeps by exactly that node's skew. It is <see langword="null"/> only when no store read happened — this node
/// holds no coordination membership, so it may not claim anything anyway.
/// </remarks>
[PublicAPI]
public sealed record EarliestTimeJobs
{
    /// <summary>Nothing pending and no store instant observed.</summary>
    public static readonly EarliestTimeJobs None = new();

    /// <summary>The store's instant at the moment the peek ran, or <see langword="null"/> when no read was made.</summary>
    public DateTime? StoreUtcNow { get; init; }

    /// <summary>
    /// Every acquirable job inside the earliest pending second, ordered by execution time, with the child hierarchy
    /// attached. Empty when nothing is due.
    /// </summary>
    public TimeJobEntity[] Jobs { get; init; } = [];
}
