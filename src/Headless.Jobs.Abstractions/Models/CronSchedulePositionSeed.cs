// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;

namespace Headless.Jobs.Models;

/// <summary>
/// A definition's initial schedule position, derived from the instant the STORE reported inside the inserting
/// transaction.
/// </summary>
/// <remarks>
/// A definition inserted without a position is positioned by whichever node encounters it first, anchored at THAT
/// encounter's instant — so every tick between creation and the first poll is silently dropped rather than resolved by
/// the definition's own missed-run policy (#817). Seeding closes that window by making creation itself the anchor.
/// </remarks>
[PublicAPI]
public readonly record struct CronSchedulePositionSeed
{
    /// <summary>The watermark: the instant through which the schedule is reconciled at creation.</summary>
    public required DateTime ReconciledThroughUtc { get; init; }

    /// <summary>The first occurrence after <see cref="ReconciledThroughUtc"/> — the indexed dispatch key.</summary>
    public required DateTime NextDueUtc { get; init; }

    /// <summary>Fingerprint of the rules the position was derived under.</summary>
    public string? EvaluationFingerprint { get; init; }
}

/// <summary>
/// Derives one definition's initial schedule position from the store instant the provider read inside the inserting
/// transaction.
/// </summary>
/// <remarks>
/// Inverted on purpose: schedule evaluation belongs to the manager (it owns the cron cache) while the clock belongs to
/// the store, so the provider calls back with its anchor instead of the manager guessing one from its own clock. Mirrors
/// the occurrence-factory callback <c>UpdateCronJobsAtomicallyAsync</c> already takes.
/// </remarks>
/// <param name="definition">The definition being inserted.</param>
/// <param name="storeUtcNow">The store's instant, read inside the inserting transaction.</param>
[PublicAPI]
public delegate CronSchedulePositionSeed CronSchedulePositionSeeder(CronJobEntity definition, DateTime storeUtcNow);

/// <summary>
/// What a seeded insert actually persisted: the store anchor it used and the earliest position it wrote.
/// </summary>
/// <remarks>
/// Callers arm their scheduler restart from <see cref="EarliestNextDueUtc"/> rather than from a locally computed
/// projection. Keeping a node-computed copy alive as a second source of truth is what lets the row and the wake disagree
/// under clock skew — the row would carry the store's projection while the restart arbitrated against this node's.
/// </remarks>
[PublicAPI]
public sealed record CronSchedulePositionSeedResult
{
    /// <summary>Nothing was inserted.</summary>
    public static readonly CronSchedulePositionSeedResult Empty = new() { StoreUtcNow = null, AffectedRows = 0 };

    /// <summary>
    /// The store instant the seed was anchored on, or <see langword="null"/> when nothing was inserted.
    /// </summary>
    public required DateTime? StoreUtcNow { get; init; }

    /// <summary>Rows written.</summary>
    public required int AffectedRows { get; init; }

    /// <summary>
    /// The earliest <see cref="CronSchedulePositionSeed.NextDueUtc"/> persisted by this insert, in the STORE's clock
    /// domain, or <see langword="null"/> when nothing was inserted.
    /// </summary>
    public DateTime? EarliestNextDueUtc { get; init; }
}
