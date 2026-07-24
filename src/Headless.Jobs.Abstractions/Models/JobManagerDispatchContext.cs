// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// SPI projection of a cron job definition that the manager dispatches to a persistence provider to generate and
/// manage occurrences. Carries only the fields the scheduler loop needs, without loading the full entity graph.
/// </summary>
[PublicAPI]
public class JobManagerDispatchContext(Guid id)
{
    /// <summary>Identifier of the cron job definition.</summary>
    public Guid Id { get; set; } = id;

    /// <summary>Registered function name; must match a <c>[JobFunction]</c>-annotated method.</summary>
    public required string FunctionName { get; set; }

    /// <summary>Six-field NCrontab expression that governs occurrence generation.</summary>
    public required string Expression { get; set; }

    /// <summary>Optional IANA timezone identifier used to evaluate <see cref="Expression"/>.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>Whether the authoritative definition was paused when this dispatch projection was read.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Monotonic schedule version used to reject stale occurrence materialization.</summary>
    public long ScheduleRevision { get; set; }

    /// <summary>Maximum number of retry attempts on failure.</summary>
    public int Retries { get; set; }

    /// <summary>Per-attempt retry delay in seconds; <see langword="null"/> uses the default backoff.</summary>
    public int[]? RetryIntervals { get; set; }

    /// <summary>Node-death policy carried from the cron definition to stamp onto generated occurrences.</summary>
    public NodeDeathPolicy OnNodeDeath { get; set; } = NodeDeathPolicy.Retry;

    /// <summary>
    /// The most recently generated upcoming occurrence for this definition, when one exists.
    /// Used by the scheduler to avoid duplicate occurrence generation on restart.
    /// </summary>
    public NextCronOccurrence? NextCronOccurrence { get; set; }
}

/// <summary>Minimal projection of the most recent upcoming occurrence for a cron job definition.</summary>
[PublicAPI]
public class NextCronOccurrence(Guid id, DateTime dateCreated)
{
    /// <summary>Identifier of the upcoming occurrence row.</summary>
    public Guid Id { get; set; } = id;

    /// <summary>UTC timestamp when the occurrence row was created.</summary>
    public DateTime DateCreated { get; set; } = dateCreated;
}
