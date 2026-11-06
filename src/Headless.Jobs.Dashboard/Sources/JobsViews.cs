// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;

namespace Headless.Jobs;

/// <summary>The object kinds the Jobs module shows, and how its rows read as the shell's objects and changes.</summary>
internal static class JobsViews
{
    /// <summary>A time job or a cron occurrence; its stream topic is <c>jobs.run</c>.</summary>
    public const string RunKind = "run";

    /// <summary>A cron job definition.</summary>
    public const string CronJobKind = "cron-job";

    /// <summary>A registered function, identified by its name.</summary>
    public const string FunctionKind = "function";

    /// <summary>The version a removal carries: above any marker, so no earlier change of the row follows it.</summary>
    public const string RemovedVersion = "9223372036854775807";

    public static ObjectRef Ref(string moduleId, string kind, string id) =>
        new()
        {
            Module = moduleId,
            Kind = kind,
            Id = id,
        };

    public static DashboardObject ToObject(string moduleId, JobRunSummary run) =>
        new()
        {
            Target = Ref(moduleId, RunKind, run.Id.ToString()),
            Title = run.Function,
            Status = run.Status.ToString(),
            TraceId = TraceIdOf(run.TraceParent),
        };

    public static DashboardObject ToObject(string moduleId, CronJobEntity cronJob) =>
        new()
        {
            Target = Ref(moduleId, CronJobKind, cronJob.Id.ToString()),
            Title = string.IsNullOrWhiteSpace(cronJob.Description)
                ? $"{cronJob.Function} ({cronJob.Expression})"
                : cronJob.Description,
            Status = cronJob.IsPaused ? "Paused" : "Active",
        };

    public static DashboardChange Change(string moduleId, JobRunSummary run, bool affectsAttention) =>
        new()
        {
            Target = Ref(moduleId, RunKind, run.Id.ToString()),
            Version = run.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture),
            Projection = JsonSerializer.SerializeToElement(
                JobsRunView.From(run),
                JobsDashboardJsonContext.Default.JobsRunView
            ),
            AffectsAttention = affectsAttention,
        };

    public static DashboardChange Change(string moduleId, CronJobEntity cronJob, CronSchedulePreview? preview) =>
        new()
        {
            Target = Ref(moduleId, CronJobKind, cronJob.Id.ToString()),
            Version = cronJob.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture),
            Projection = JsonSerializer.SerializeToElement(
                JobsCronJobView.From(cronJob, preview, nextFires: []),
                JobsDashboardJsonContext.Default.JobsCronJobView
            ),
            // A paused cron job and a resumed one change what Needs you shows about its function.
            AffectsAttention = true,
        };

    /// <summary>
    /// Reads a changed row back for its projection, or returns <see langword="null"/> when the row is gone.
    /// </summary>
    public static async Task<DashboardChange?> ReadBackAsync(
        IJobsDashboardBackend backend,
        TimeProvider timeProvider,
        string moduleId,
        JobChangeKind kind,
        Guid id,
        bool affectsAttention,
        CancellationToken cancellationToken
    )
    {
        if (kind == JobChangeKind.CronJob)
        {
            var cronJob = await backend.GetCronJobAsync(id, cancellationToken).ConfigureAwait(false);

            if (cronJob is null)
            {
                return null;
            }

            var preview = backend.Preview(
                cronJob.Expression,
                cronJob.TimeZoneId,
                timeProvider.GetUtcNow().UtcDateTime,
                count: 1
            );

            return Change(moduleId, cronJob, preview);
        }

        var run = await backend.GetRunAsync(id, cancellationToken).ConfigureAwait(false);

        return run is null ? null : Change(moduleId, run, affectsAttention);
    }

    /// <summary>The object kind a change of the given row kind targets.</summary>
    public static string KindOf(JobChangeKind kind) => kind == JobChangeKind.CronJob ? CronJobKind : RunKind;

    /// <summary>A change that makes views refetch an object that is gone, so they show it no longer exists.</summary>
    public static DashboardChange Removed(string moduleId, string kind, Guid id) =>
        new()
        {
            Target = Ref(moduleId, kind, id.ToString()),
            Version = RemovedVersion,
            Projection = null,
            AffectsAttention = true,
        };

    /// <summary>The trace id of a W3C <c>traceparent</c>, or <see langword="null"/> when there is none.</summary>
    public static string? TraceIdOf(string? traceParent)
    {
        // version "-" 32 hex trace id "-" 16 hex span id "-" flags
        if (traceParent is not { Length: >= 55 } || traceParent[2] != '-' || traceParent[35] != '-')
        {
            return null;
        }

        var traceId = traceParent.Substring(3, 32);

        return traceId.All(char.IsAsciiHexDigit) && traceId.Any(static c => c != '0') ? traceId : null;
    }

    /// <summary>
    /// The message and top frame of a stored exception. The executor stores a failure as JSON (the outer message and
    /// the root exception's top frame), so the operator reads text instead of the stored JSON; a reason the store
    /// wrote itself (lease lapsed, node not alive) or a cancellation message is plain text and reads as it is.
    /// </summary>
    public static (string? Message, string? Frame) ReadException(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith('{'))
        {
            return (stored, null);
        }

        try
        {
            var detail = JsonSerializer.Deserialize(
                stored,
                JobsExceptionJsonContext.Default.ExceptionDetailClassForSerialization
            );

            if (detail is null)
            {
                return (stored, null);
            }

            var frame = string.IsNullOrWhiteSpace(detail.StackTrace) ? null : detail.StackTrace.TrimEnd();

            return (detail.Message, frame);
        }
        catch (JsonException)
        {
            // Text that only starts like JSON is a plain message, shown as stored.
            return (stored, null);
        }
    }
}

/// <summary>A run as a list row, a stream change, and the inspector show it.</summary>
internal sealed record JobsRunView(
    Guid Id,
    JobRunKind Kind,
    string Function,
    string? ContractVersion,
    JobStatus Status,
    DateTime? ExecutionTime,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExecutedAt,
    long ElapsedMilliseconds,
    int RetryCount,
    string? Node,
    string? ExceptionMessage,
    string? ExceptionFrame,
    string? FailureFingerprint,
    string? SkippedReason,
    double? ProgressPercent,
    string? ProgressMessage,
    string? CorrelationId,
    string? CausationId,
    string? TraceId,
    string? TenantId,
    string? Description,
    Guid? CronJobId,
    Guid? ParentId,
    string? BusinessKey,
    bool CancelRequested,
    string Version
)
{
    public static JobsRunView From(JobRunSummary run)
    {
        var (message, frame) = JobsViews.ReadException(run.ExceptionMessage);

        return new(
            run.Id,
            run.Kind,
            run.Function,
            run.ContractVersion,
            run.Status,
            run.ExecutionTime,
            run.CreatedAt,
            run.ExecutedAt,
            run.ElapsedTime,
            run.RetryCount,
            run.OwnerId,
            message,
            frame,
            run.FailureFingerprint,
            run.SkippedReason,
            run.ProgressPercent,
            run.ProgressMessage,
            run.CorrelationId,
            run.CausationId,
            JobsViews.TraceIdOf(run.TraceParent),
            run.TenantId,
            run.Description,
            run.CronJobId,
            run.ParentId,
            run.BusinessKey,
            run.CancelRequested,
            run.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture)
        );
    }
}

/// <summary>A run with its chain children, as the run page shows it.</summary>
internal sealed record JobsRunDetail(JobsRunView Run, IReadOnlyList<Guid> Children);

/// <summary>One page of runs, a count capped at <see cref="JobRunCount.DefaultCap"/>, and the next page's cursor.</summary>
internal sealed record JobsRunList(IReadOnlyList<JobsRunView> Items, int Count, bool CountCapped, string? NextCursor);

/// <summary>One attempt of a run: the node that ran it, when, and how it ended.</summary>
internal sealed record JobsAttemptView(
    int Number,
    string? Node,
    DateTime? StartedAt,
    DateTime? EndedAt,
    JobAttemptOutcome? Outcome,
    string? ExceptionMessage,
    string? ExceptionFrame,
    string? FailureFingerprint
)
{
    public static JobsAttemptView From(JobAttemptEntity attempt)
    {
        var (message, frame) = JobsViews.ReadException(attempt.ExceptionMessage);

        return new(
            attempt.Number,
            attempt.OwnerId,
            attempt.StartedAt,
            attempt.EndedAt,
            attempt.Outcome,
            message,
            frame,
            attempt.FailureFingerprint
        );
    }
}

internal sealed record JobsAttemptList(IReadOnlyList<JobsAttemptView> Items);

/// <summary>A cron job as its list row and page show it; the next fires are filled on the page only.</summary>
internal sealed record JobsCronJobView(
    Guid Id,
    string Function,
    string? Description,
    string Expression,
    string? Reading,
    string TimeZoneId,
    bool IsPaused,
    bool DefinedInCode,
    CronOverlapPolicy OnOverlap,
    int Retries,
    DateTime NextDueUtc,
    string? ValidationMessage,
    IReadOnlyList<DateTime> NextFires,
    string Version
)
{
    public static JobsCronJobView From(
        CronJobEntity cronJob,
        CronSchedulePreview? preview,
        IReadOnlyList<DateTime> nextFires
    ) =>
        new(
            cronJob.Id,
            cronJob.Function,
            cronJob.Description,
            cronJob.Expression,
            preview?.Reading,
            cronJob.TimeZoneId ?? string.Empty,
            cronJob.IsPaused,
            JobsRules.IsDefinedInCode(cronJob),
            cronJob.OnOverlap,
            cronJob.Retries,
            cronJob.NextDueUtc,
            preview?.ValidationMessage,
            nextFires,
            cronJob.ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture)
        );
}

internal sealed record JobsCronJobList(IReadOnlyList<JobsCronJobView> Items);

/// <summary>What a stored or draft schedule would do, in the zone it fires in.</summary>
internal sealed record JobsSchedulePreview(
    string TimeZoneId,
    string? Reading,
    IReadOnlyList<DateTime> NextFires,
    string? ValidationMessage
);

/// <summary>A registered function with what runs it and how its runs went in the last 24 hours.</summary>
internal sealed record JobsFunctionView(
    string Name,
    string ContractVersion,
    JobPriority Priority,
    int MaxConcurrency,
    string? RequestType,
    string? CronExpression,
    IReadOnlyList<string> ClaimedBy,
    int Succeeded,
    int Failed,
    int Running,
    int Waiting,
    DateTimeOffset? LastExecutedAt,
    IReadOnlyList<Guid> CronJobs
);

internal sealed record JobsFunctionList(IReadOnlyList<JobsFunctionView> Items);

/// <summary>A live node and the functions its run filter claims, or <see langword="null"/> when it published none.</summary>
internal sealed record JobsNodeView(
    string Identity,
    string State,
    string? Role,
    IReadOnlyList<string>? ClaimedFunctions
);

internal sealed record JobsNodeList(IReadOnlyList<JobsNodeView> Items);

/// <summary>The id a create call returns.</summary>
internal sealed record JobsCreated(Guid Id);

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
        };
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(JobsRunView))]
[JsonSerializable(typeof(JobsRunDetail))]
[JsonSerializable(typeof(JobsRunList))]
[JsonSerializable(typeof(JobsAttemptList))]
[JsonSerializable(typeof(JobsCronJobView))]
[JsonSerializable(typeof(JobsCronJobList))]
[JsonSerializable(typeof(JobsSchedulePreview))]
[JsonSerializable(typeof(JobsFunctionView))]
[JsonSerializable(typeof(JobsFunctionList))]
[JsonSerializable(typeof(JobsNodeList))]
[JsonSerializable(typeof(JobsCreated))]
[JsonSerializable(typeof(JobsTimeJobBody))]
[JsonSerializable(typeof(JobsCronJobBody))]
[JsonSerializable(typeof(JobsRunScope))]
internal sealed partial class JobsDashboardJsonContext : JsonSerializerContext;
