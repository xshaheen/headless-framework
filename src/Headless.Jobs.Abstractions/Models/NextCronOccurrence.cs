// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>Minimal projection of the most recent upcoming occurrence for a cron job definition.</summary>
[PublicAPI]
public class NextCronOccurrence(Guid id, DateTimeOffset createdAt)
{
    /// <summary>Identifier of the upcoming occurrence row.</summary>
    public Guid Id { get; set; } = id;

    /// <summary>UTC timestamp when the occurrence row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = createdAt;

    /// <summary>
    /// The first unaccounted-for missed instant this row stands in for when it was materialized or repurposed by
    /// misfire recovery; <see langword="null"/> for an ordinary occurrence.
    /// </summary>
    /// <remarks>
    /// Carried here because every claim strategy reconstructs the claimed entity by hand rather than re-reading the
    /// row. Without it the stamp survives the store but is dropped on the way to execution, which silently demotes a
    /// coalesced run to an ordinary one — the same shape as a dropped retry counter.
    /// </remarks>
    public DateTime? RecoveredFromUtc { get; set; }
}
