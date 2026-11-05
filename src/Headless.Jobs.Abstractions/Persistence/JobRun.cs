// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.InteropServices;

namespace Headless.Jobs;

/// <summary>The kind of a run: a time job or one occurrence of a cron job.</summary>
[PublicAPI]
#pragma warning disable CA1008 // Every run is one of the two kinds, so a zero member would name none.
public enum JobRunKind
{
    /// <summary>A time job, a root or a chain member.</summary>
    TimeJob = 1,

    /// <summary>One occurrence of a cron job.</summary>
    CronJobOccurrence = 2,
}
#pragma warning restore CA1008

/// <summary>
/// Which runs a run read covers: time jobs and cron occurrences together, narrowed by each filter that is set. A filter
/// left <see langword="null"/> or empty matches every run.
/// </summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobRunQuery
{
    /// <summary>Gets the one kind to read, or <see langword="null"/> for both.</summary>
    public JobRunKind? Kind { get; init; }

    /// <summary>Gets the functions to read, compared ordinally; <see langword="null"/> or empty for every function.</summary>
    public IReadOnlyCollection<string>? Functions { get; init; }

    /// <summary>Gets the statuses to read; <see langword="null"/> or empty for every status.</summary>
    public IReadOnlyCollection<JobStatus>? Statuses { get; init; }

    /// <summary>
    /// Gets the inclusive lower bound of the execution time (UTC). A run without an execution time, a chain member
    /// waiting on its parent, is outside every bounded range.
    /// </summary>
    public DateTime? ExecutionFrom { get; init; }

    /// <summary>Gets the exclusive upper bound of the execution time (UTC).</summary>
    public DateTime? ExecutionBefore { get; init; }

    /// <summary>Gets the correlation id every run read must carry, compared ordinally.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the cron job whose occurrences to read; set, it excludes time jobs.</summary>
    public Guid? CronJobId { get; init; }

    /// <summary>Gets the failure fingerprint every run read must carry.</summary>
    public string? FailureFingerprint { get; init; }

    /// <summary>
    /// Gets a value indicating whether to read only actionable failures, as <see cref="JobRequeueRules"/> defines them:
    /// failed runs a requeue accepts, without failed cron occurrences a later occurrence of the same cron job succeeded
    /// after.
    /// </summary>
    public bool ActionableFailuresOnly { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run list reads the latest execution time first. A cursor is valid only
    /// with the order it was read in.
    /// </summary>
    public bool NewestFirst { get; init; }

    /// <summary>Gets a value indicating whether the query reads time jobs.</summary>
    public bool IncludesTimeJobs => Kind is null or JobRunKind.TimeJob && CronJobId is null;

    /// <summary>Gets a value indicating whether the query reads cron occurrences.</summary>
    public bool IncludesCronJobOccurrences => Kind is null or JobRunKind.CronJobOccurrence;
}

/// <summary>One run, a time job or a cron occurrence, as the run reads return it.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobRunSummary
{
    /// <summary>Gets the run's kind.</summary>
    public required JobRunKind Kind { get; init; }

    /// <summary>Gets the run's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the job function the run invokes.</summary>
    public required string Function { get; init; }

    /// <summary>Gets the payload contract version the run carries.</summary>
    public string? ContractVersion { get; init; }

    /// <summary>Gets the run's status.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>
    /// Gets when the run is due (UTC), or <see langword="null"/> for a chain member waiting on its parent.
    /// </summary>
    public DateTime? ExecutionTime { get; init; }

    /// <summary>Gets when the run was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Gets the run's change marker, <see cref="BaseJobEntity.ChangedAt"/>.</summary>
    public DateTime ChangedAt { get; init; }

    /// <summary>Gets when the run's last execution ended, or <see langword="null"/>.</summary>
    public DateTimeOffset? ExecutedAt { get; init; }

    /// <summary>Gets the last execution's elapsed time in milliseconds.</summary>
    public long ElapsedTime { get; init; }

    /// <summary>Gets the number of retries the run used.</summary>
    public int RetryCount { get; init; }

    /// <summary>Gets the node that holds or last held the run.</summary>
    public string? OwnerId { get; init; }

    /// <summary>Gets the stored exception of the last failure.</summary>
    public string? ExceptionMessage { get; init; }

    /// <summary>Gets the failure fingerprint of the last failure.</summary>
    public string? FailureFingerprint { get; init; }

    /// <summary>Gets why the run was skipped.</summary>
    public string? SkippedReason { get; init; }

    /// <summary>Gets the last reported progress, from 0 to 100.</summary>
    public double? ProgressPercent { get; init; }

    /// <summary>Gets the last reported progress message.</summary>
    public string? ProgressMessage { get; init; }

    /// <summary>Gets the run's correlation id.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the run's causation id.</summary>
    public string? CausationId { get; init; }

    /// <summary>Gets the W3C <c>traceparent</c> of the request that caused the run.</summary>
    public string? TraceParent { get; init; }

    /// <summary>Gets the run's tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets a time job's description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets a cron occurrence's cron job.</summary>
    public Guid? CronJobId { get; init; }

    /// <summary>Gets a chain member's parent.</summary>
    public Guid? ParentId { get; init; }

    /// <summary>Gets a keyed time job's business key.</summary>
    public string? BusinessKey { get; init; }

    /// <summary>Gets a value indicating whether cancellation of a time job was requested.</summary>
    public bool CancelRequested { get; init; }
}

/// <summary>The keyset position of one run in a run read: its execution-time sort key, then its identifier.</summary>
/// <remarks>
/// A run without an execution time sorts at <see cref="Unscheduled"/>. Stores order identifiers differently, so a
/// position is meaningful only to the store that produced it.
/// </remarks>
/// <param name="ExecutionTime">The run's execution time, or <see cref="Unscheduled"/>.</param>
/// <param name="Id">The run's identifier.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobRunPosition(DateTime ExecutionTime, Guid Id)
{
    /// <summary>
    /// The sort key of a run without an execution time: after every scheduled run. A fixed date rather than
    /// <see cref="DateTime.MaxValue"/>, which some stores map to infinity or cannot store.
    /// </summary>
    public static readonly DateTime Unscheduled = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);
}

/// <summary>
/// Where a run list resumes: the last position the list returned in each kind's table. Each table pages forward on its
/// own keyset, and pages merge by execution time, so no run repeats or goes missing at a page boundary.
/// </summary>
/// <remarks>The cursor round-trips through <see cref="ToString"/> and <see cref="TryParse"/>.</remarks>
[PublicAPI]
public sealed record JobRunCursor
{
    private const string _FormatVersion = "r1";
    private const char _Separator = ':';
    private const string _NoPosition = "-";

    /// <summary>Gets the last time job the list returned, or <see langword="null"/> before the first.</summary>
    public JobRunPosition? TimeJobs { get; init; }

    /// <summary>Gets the last cron occurrence the list returned, or <see langword="null"/> before the first.</summary>
    public JobRunPosition? CronJobOccurrences { get; init; }

    /// <summary>Returns the position the list reached in the table of <paramref name="kind"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public JobRunPosition? PositionOf(JobRunKind kind)
    {
        return kind switch
        {
            JobRunKind.TimeJob => TimeJobs,
            JobRunKind.CronJobOccurrence => CronJobOccurrences,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown run kind."),
        };
    }

    /// <summary>Returns this cursor with the position in the table of <paramref name="kind"/> moved.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value.</exception>
    public JobRunCursor WithPosition(JobRunKind kind, JobRunPosition position)
    {
        return kind switch
        {
            JobRunKind.TimeJob => this with { TimeJobs = position },
            JobRunKind.CronJobOccurrence => this with { CronJobOccurrences = position },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown run kind."),
        };
    }

    /// <summary>Formats the cursor as a string <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        return string.Join(_Separator, _FormatVersion, _Format(TimeJobs), _Format(CronJobOccurrences));
    }

    /// <summary>Reads a cursor <see cref="ToString"/> formatted.</summary>
    /// <param name="value">The formatted cursor.</param>
    /// <param name="cursor">The cursor, or <see langword="null"/> when <paramref name="value"/> is not one.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a cursor.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out JobRunCursor? cursor)
    {
        cursor = null;
        var parts = value?.Split(_Separator);

        if (
            parts is not { Length: 3 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !_TryParsePosition(parts[1], out var timeJobs)
            || !_TryParsePosition(parts[2], out var occurrences)
        )
        {
            return false;
        }

        cursor = new JobRunCursor { TimeJobs = timeJobs, CronJobOccurrences = occurrences };

        return true;
    }

    private static string _Format(JobRunPosition? position)
    {
        return position is { } value
            ? value.ExecutionTime.Ticks.ToString(CultureInfo.InvariantCulture) + "_" + value.Id.ToString("N")
            : _NoPosition;
    }

    private static bool _TryParsePosition(string value, out JobRunPosition? position)
    {
        position = null;

        if (string.Equals(value, _NoPosition, StringComparison.Ordinal))
        {
            return true;
        }

        var separator = value.IndexOf('_', StringComparison.Ordinal);

        if (
            separator < 0
            || !long.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks
            || !Guid.TryParseExact(value[(separator + 1)..], "N", out var id)
        )
        {
            return false;
        }

        position = new JobRunPosition(new DateTime(ticks, DateTimeKind.Utc), id);

        return true;
    }
}

/// <summary>One page of a run list and the cursor of the next page.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobRunPage
{
    /// <summary>Gets the runs, in execution-time order.</summary>
    public required IReadOnlyList<JobRunSummary> Runs { get; init; }

    /// <summary>Gets the cursor of the next page, or <see langword="null"/> when this page is the last.</summary>
    public JobRunCursor? Next { get; init; }
}

/// <summary>A count that stops at a cap: a count above it reports the cap with <see cref="Capped"/> set.</summary>
/// <param name="Count">The count, at most the cap.</param>
/// <param name="Capped">Whether more rows matched than the cap.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobRunCount(int Count, bool Capped)
{
    /// <summary>The cap the dashboard counts to: a larger count only says "more than this many".</summary>
    public const int DefaultCap = 10_000;

    /// <summary>Returns the count of <paramref name="matched"/> rows stopped at <paramref name="cap"/>.</summary>
    /// <param name="matched">How many rows the store counted, which may exceed the cap by the one row that proves it.</param>
    /// <param name="cap">The cap.</param>
    public static JobRunCount Of(long matched, int cap)
    {
        return matched > cap ? new JobRunCount(cap, Capped: true) : new JobRunCount((int)matched, Capped: false);
    }
}

/// <summary>The runs of one 24-hour window, as the Overview figures count them.</summary>
/// <param name="Succeeded">Runs whose last execution succeeded in the window.</param>
/// <param name="Failed">Runs whose last execution failed in the window.</param>
/// <param name="Late">Runs whose first attempt started in the window later than the late threshold after they were due.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobRunWindowFigures(int Succeeded, int Failed, int Late)
{
    /// <summary>
    /// Gets the share of finished runs that succeeded, from 0 to 1, or <see langword="null"/> when no run finished.
    /// </summary>
    public double? SucceededShare => Succeeded + Failed == 0 ? null : (double)Succeeded / (Succeeded + Failed);
}
