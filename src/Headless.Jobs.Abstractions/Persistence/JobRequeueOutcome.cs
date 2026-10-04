// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// The outcome of one requeue request. Every value other than <see cref="Requeued"/> is a refusal that left the
/// stored row unchanged, so an operator can tell why nothing happened without parsing an exception. The zero value is
/// a refusal, so an uninitialized outcome never reads as a successful requeue.
/// </summary>
[PublicAPI]
public enum JobRequeueOutcome
{
    /// <summary>No row has the requested identifier.</summary>
    NotFound = 0,

    /// <summary>The row moved from <c>Failed</c> back to <c>Idle</c> and will run again.</summary>
    Requeued = 1,

    /// <summary>
    /// The row is not <c>Failed</c>. A repeated request for a row that was already requeued lands here, so requeue is
    /// safe to repeat.
    /// </summary>
    NotFailed = 2,

    /// <summary>
    /// The time job belongs to a chain. A parent's children may already have resolved against its failure, and a
    /// requeued child would wait for a parent run that never comes, so chain members are not requeued.
    /// </summary>
    ChainMember = 3,

    /// <summary>
    /// The keyed time job is no longer its key's current generation. Requeuing it would run a second live job for one
    /// business key.
    /// </summary>
    SupersededGeneration = 4,

    /// <summary>
    /// The cron occurrence's definition forbids overlapping runs and another occurrence of that definition is still
    /// unfinished.
    /// </summary>
    Overlap = 5,

    /// <summary>
    /// The row changed while the request ran, or another live occurrence already holds the occurrence's instant.
    /// </summary>
    Conflict = 6,
}
