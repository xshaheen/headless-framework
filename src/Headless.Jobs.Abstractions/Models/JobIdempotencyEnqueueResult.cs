// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>
/// The outcome of one idempotent enqueue: the effective job identifier and whether this call created it. A
/// dedup hit returns the first caller's job ID with <see cref="Created"/> = <see langword="false"/> — the
/// scheduler surfaces the same ID either way, and only side-effect arming differs.
/// </summary>
/// <param name="JobId">The job the idempotency key currently owns; may outlive the job row.</param>
/// <param name="Created"><see langword="true"/> when this call inserted the job and opened the reservation.</param>
[PublicAPI]
public sealed record JobIdempotencyEnqueueResult(Guid JobId, bool Created)
{
    /// <summary>
    /// The observation belongs to the caller transaction; its durable effect depends on the outer commit. Set
    /// on the coordinated path, where a concurrent same-key caller blocked on the reservation lock observes this
    /// caller's state only after that commit.
    /// </summary>
    public bool IsProvisional { get; init; }
}
