// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>
/// Internal projection of a cron job definition used by the scheduler to generate and manage occurrences.
/// Carries the fields needed by the scheduler loop without loading the full entity graph.
/// </summary>
public class InternalManagerContext(Guid id)
{
    /// <summary>Identifier of the cron job definition.</summary>
    public Guid Id { get; set; } = id;

    /// <summary>Registered function name; must match a <c>[JobFunction]</c>-annotated method.</summary>
    public required string FunctionName { get; set; }

    /// <summary>Six-field NCrontab expression that governs occurrence generation.</summary>
    public required string Expression { get; set; }

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
public class NextCronOccurrence(Guid id, DateTime createdAt)
{
    /// <summary>Identifier of the upcoming occurrence row.</summary>
    public Guid Id { get; set; } = id;

    /// <summary>UTC timestamp when the occurrence row was created.</summary>
    public DateTime CreatedAt { get; set; } = createdAt;
}
