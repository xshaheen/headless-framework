// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Why a cron occurrence row left the live lifecycle, expressed as a typed value the occupied-instant rule can read.
/// </summary>
/// <remarks>
/// This is the SOLE accounting input for whether a row stands for its <c>(CronJobId, ExecutionTime)</c> instant.
/// <c>SkippedReason</c> is human-facing display text and must never be matched to decide accounting: two producers
/// write the identical string <c>"Cron definition updated"</c> yet owe opposite answers, so string equality cannot
/// express the rule and neither the compiler nor the schema can enforce it.
/// </remarks>
[PublicAPI]
public enum CronOccurrenceDisposition
{
    /// <summary>
    /// Default. The row stands for its instant: it ran, is running, is queued to run, or was retired by a producer
    /// that owes nothing further (pause, recovery, dead-node sweep, lapsed lease, user-code skip). Existing rows
    /// predating the disposition column carry this value, which preserves their prior behaviour.
    /// </summary>
    Accounted = 0,

    /// <summary>
    /// The startup seeding migration retired the row when a redeployed definition changed expression, WITHOUT
    /// creating a replacement and without advancing anything that would re-derive the instant. The fire is still
    /// owed, so this is the one disposition that lets the instant be materialized again.
    /// </summary>
    ReplacementOwed = 1,

    /// <summary>
    /// A runtime schedule edit through <c>ICronJobManager</c> retired the row. That path rebases the definition's
    /// projection and creates the replacement occurrence itself (or leaves a paused definition deliberately idle
    /// until resume), so the new schedule already owns what happens next and re-firing the old instant would
    /// double-run the edit. Suppresses, exactly like <see cref="Accounted" />, and is kept distinct from it only to
    /// preserve provenance.
    /// </summary>
    Superseded = 2,
}
