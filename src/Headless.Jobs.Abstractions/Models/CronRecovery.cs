// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// Applies a recovery policy to a cron definition whose watermark fell behind: resolves whatever occurrences already
/// sit in the missed window, materializes the policy's run (or none), and carries the watermark to the recovery
/// instant — as one step, so no interleaving can leave the backlog half-resolved.
/// </summary>
[PublicAPI]
public sealed record CronRecoveryRequest
{
    /// <summary>The cron definition entering recovery.</summary>
    public required Guid CronJobId { get; init; }

    /// <summary>The exact durable watermark the caller observed. Also the exclusive lower bound of the missed window.</summary>
    public required DateTime ObservedReconciledThroughUtc { get; init; }

    /// <summary>The exact durable schedule revision the caller observed.</summary>
    public required long ExpectedScheduleRevision { get; init; }

    /// <summary>
    /// The recovery instant — the store instant recovery ran at. Becomes the new watermark and the inclusive upper
    /// bound of the missed window, so the backlog it resolved is never reconsidered.
    /// </summary>
    public required DateTime RecoveredThroughUtc { get; init; }

    /// <summary>The projection to persist: the first occurrence after <see cref="RecoveredThroughUtc"/>.</summary>
    public required DateTime NextDueUtc { get; init; }

    /// <summary>Which policy resolves the backlog.</summary>
    public required MissedRunPolicy Policy { get; init; }

    /// <summary>
    /// First occurrence after the observed watermark. The coalesced run's scheduled instant and its durable recovery
    /// stamp, and exact even when the backlog count saturated, because it is the first instant the walk visits.
    /// </summary>
    public required DateTime EarliestMissedUtc { get; init; }

    /// <summary>Identifier to use when the coalesced run has to be created rather than repurposed.</summary>
    public required Guid CoalescedOccurrenceId { get; init; }

    /// <summary>Node-death policy stamped onto a materialized or repurposed run.</summary>
    public NodeDeathPolicy OnNodeDeath { get; init; } = NodeDeathPolicy.Retry;

    /// <summary>Observational timestamp for audit fields on rows this recovery touches.</summary>
    public required DateTimeOffset OperationTimeUtc { get; init; }
}

/// <summary>What a recovery actually did, read back from the store.</summary>
/// <typeparam name="TCronJob">The application's concrete cron job entity type.</typeparam>
[PublicAPI]
public sealed record CronRecoveryResult<TCronJob>
    where TCronJob : CronJobEntity, new()
{
    /// <summary>
    /// The single run standing in for the backlog, or <see langword="null"/> under skip — and also under coalesce when
    /// the earliest missed instant was already occupied, since a second row there would duplicate it.
    /// </summary>
    public CronJobOccurrenceEntity<TCronJob>? CoalescedRun { get; init; }

    /// <summary>Not-yet-executing occurrences in the missed window transitioned to skipped.</summary>
    public required int SkippedOccurrenceCount { get; init; }

    /// <summary>The persisted watermark after recovery.</summary>
    public required DateTime ReconciledThroughUtc { get; init; }

    /// <summary>The persisted projection after recovery.</summary>
    public required DateTime NextDueUtc { get; init; }
}
