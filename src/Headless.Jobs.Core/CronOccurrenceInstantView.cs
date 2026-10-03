// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;

namespace Headless.Jobs;

/// <summary>
/// One cron occurrence row reduced to the three booleans every occupied-instant decision needs, with the raw
/// <c>Status</c> deliberately absent.
/// </summary>
/// <remarks>
/// Status is persisted as a string-backed enum, so a value written by a newer binary would throw on materialization
/// if it were projected as <see cref="JobStatus" />. Every flag here is computed by the database (or by the compiled
/// projector in-memory) from string comparisons that cannot throw, and an unrecognized status therefore lands as
/// "not live, not repurposable, accounts for its instant" — the fail-closed answer.
/// </remarks>
[PublicAPI]
public sealed class CronOccurrenceInstantView
{
    /// <summary>Identity of the occurrence row.</summary>
    public Guid Id { get; init; }

    /// <summary>The definition this row belongs to, so one read can answer for a whole claim wave.</summary>
    public Guid CronJobId { get; init; }

    /// <summary>The instant this row stands at.</summary>
    public DateTime ExecutionTime { get; init; }

    /// <summary>Creation timestamp, used as the stable tiebreak when several rows share an instant.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The recovery stamp carried by the row, or <see langword="null" /> for an ordinary dispatch.</summary>
    public DateTime? RecoveredFromUtc { get; init; }

    /// <summary>Whether the row is still in the live lifecycle (<c>Idle</c>, <c>Queued</c>, or <c>InProgress</c>).</summary>
    public bool IsLive { get; init; }

    /// <summary>Whether recovery may repurpose the row in place (<c>Idle</c> or <c>Queued</c> — never <c>InProgress</c>).</summary>
    public bool IsRepurposable { get; init; }

    /// <summary>Whether this row stands for its instant, so no further occurrence may be materialized there.</summary>
    public bool AccountsForInstant { get; init; }
}
