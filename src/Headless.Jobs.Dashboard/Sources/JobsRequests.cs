// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>The body of <c>POST time-jobs</c>: a time job, or a chain when it has children.</summary>
internal sealed record JobsTimeJobBody
{
    public string? Function { get; init; }

    /// <summary>The due time. Without an offset it is a wall-clock time in <see cref="TimeZoneId"/>, else UTC.</summary>
    public DateTime? ExecutionTime { get; init; }

    public string? TimeZoneId { get; init; }

    public JsonElement? Request { get; init; }

    public string? Description { get; init; }

    public int Retries { get; init; }

    public int[]? RetryIntervals { get; init; }

    public RunCondition? RunCondition { get; init; }

    public IReadOnlyList<JobsTimeJobBody>? Children { get; init; }
}

/// <summary>The body of <c>POST cron-jobs</c>.</summary>
internal sealed record JobsCronJobBody
{
    public string? Function { get; init; }

    public string? Expression { get; init; }

    public string? TimeZoneId { get; init; }

    public JsonElement? Request { get; init; }

    public string? Description { get; init; }

    public int Retries { get; init; }

    public int[]? RetryIntervals { get; init; }
}

/// <summary>
/// The filter an Operation is scoped by, in the shape the Runs list sends it and a Needs-you group hands it over.
/// </summary>
internal sealed record JobsRunScope
{
    public JobRunKind? Kind { get; init; }

    public IReadOnlyList<string>? Functions { get; init; }

    public IReadOnlyList<JobStatus>? Statuses { get; init; }

    public Guid? CronJobId { get; init; }

    public string? CorrelationId { get; init; }

    public string? Fingerprint { get; init; }

    public DateTime? From { get; init; }

    public DateTime? Before { get; init; }

    /// <summary>Whether only actionable failures are in scope, as a Needs-you group's filter asks.</summary>
    public bool ActionableOnly { get; init; }

    /// <summary>The Runs list's search text, so an Operation covers the rows the operator sees and no more.</summary>
    public string? Search { get; init; }

    public JobRunQuery ToQuery() =>
        new()
        {
            Kind = Kind,
            Functions = Functions is { Count: > 0 } functions ? [.. functions] : null,
            Statuses = Statuses is { Count: > 0 } statuses ? [.. statuses] : null,
            CronJobId = CronJobId,
            CorrelationId = string.IsNullOrEmpty(CorrelationId) ? null : CorrelationId,
            FailureFingerprint = string.IsNullOrEmpty(Fingerprint) ? null : Fingerprint,
            ExecutionFrom = From is { } from ? DateTime.SpecifyKind(from, DateTimeKind.Utc) : null,
            ExecutionBefore = Before is { } before ? DateTime.SpecifyKind(before, DateTimeKind.Utc) : null,
            ActionableFailuresOnly = ActionableOnly,
            Text = string.IsNullOrEmpty(Search) ? null : Search,
        };
}
