// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;

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

/// <summary>The value a run list sorts on; runs with one value list in identifier order.</summary>
[PublicAPI]
public enum JobRunSortKey
{
    /// <summary>When the run is due, a run without an execution time sorting at <see cref="JobRunPosition.Unscheduled"/>.</summary>
    ExecutionTime = 0,

    /// <summary>The job function, compared in the store's order: its column collation, ordinal in memory.</summary>
    Function = 1,

    /// <summary>The status, in the order of <see cref="JobStatus"/>'s values: the run's lifecycle order.</summary>
    Status = 2,
}

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
    /// Gets the text every run read must contain, as a literal, case-insensitive substring of its function,
    /// identifier, business key, or description; <see langword="null"/> or empty for every run. A cron occurrence has
    /// no business key or description, so only its function and identifier match. Request payloads are never searched.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>Gets the value the run list sorts on, then the identifier. The default is the execution time.</summary>
    public JobRunSortKey SortBy { get; init; }

    /// <summary>
    /// Gets the direction of the run list's order, applied to the sort key and the identifier alike. The default is
    /// ascending. A cursor is valid only with the sort and filters it was read with.
    /// </summary>
    public ListSortDirection SortDirection { get; init; }

    /// <summary>Gets a value indicating whether the run list reads in descending order.</summary>
    public bool Descending => SortDirection == ListSortDirection.Descending;

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

    /// <summary>
    /// Gets whether an attempt still holds the run's lease: the run is claimed or running. <see cref="OwnerId"/> outlives
    /// the lease, so only this says the run is held now.
    /// </summary>
    public bool IsLeased { get; init; }

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

    /// <summary>Gets when the run last reported progress, or <see langword="null"/> when it never reported.</summary>
    public DateTimeOffset? ProgressUpdatedAt { get; init; }

    /// <summary>
    /// Gets the most retries the run may use: a time job's own setting, or a cron occurrence's cron job's.
    /// </summary>
    public int Retries { get; init; }

    /// <summary>
    /// Gets the delay before each retry in seconds, the last repeating for any further retry, or
    /// <see langword="null"/> when the function's failure policy paces them. A cron occurrence carries its cron job's.
    /// </summary>
    public IReadOnlyList<int>? RetryIntervals { get; init; }

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

/// <summary>
/// The keyset position of one run in a run read: the values it sorts on, then its identifier. A read uses the value
/// of its query's <see cref="JobRunQuery.SortBy"/> and the identifier, and ignores the others.
/// </summary>
/// <remarks>
/// A run without an execution time sorts at <see cref="Unscheduled"/>. Stores order identifiers and text differently,
/// so a position is meaningful only to the store that produced it.
/// </remarks>
/// <param name="ExecutionTime">The run's execution time, or <see cref="Unscheduled"/>.</param>
/// <param name="Function">The run's job function.</param>
/// <param name="Status">The run's status.</param>
/// <param name="Id">The run's identifier.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobRunPosition(DateTime ExecutionTime, string Function, JobStatus Status, Guid Id)
{
    /// <summary>
    /// The sort key of a run without an execution time: after every scheduled run. A fixed date rather than
    /// <see cref="DateTime.MaxValue"/>, which some stores map to infinity or cannot store.
    /// </summary>
    public static readonly DateTime Unscheduled = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Returns the position of <paramref name="run"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is <see langword="null"/>.</exception>
    public static JobRunPosition Of(JobRunSummary run)
    {
        Argument.IsNotNull(run);

        return new(run.ExecutionTime ?? Unscheduled, run.Function, run.Status, run.Id);
    }

    /// <summary>
    /// Compares the sort key <paramref name="sortBy"/> names, ascending, functions ordinally. The identifier is left
    /// out: it is the store's tie-breaker, and stores order identifiers differently.
    /// </summary>
    internal int CompareKey(JobRunSortKey sortBy, JobRunPosition other)
    {
        return sortBy switch
        {
            JobRunSortKey.ExecutionTime => ExecutionTime.CompareTo(other.ExecutionTime),
            JobRunSortKey.Function => string.CompareOrdinal(Function, other.Function),
            JobRunSortKey.Status => ((int)Status).CompareTo((int)other.Status),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, "Unknown sort key."),
        };
    }
}

/// <summary>
/// Where a run list resumes: the sort and filters it was read with, and the last position the list returned in each
/// kind's table. Each table pages forward on its own keyset, and pages merge by the sort key, so no run repeats or
/// goes missing at a page boundary.
/// </summary>
/// <remarks>
/// The cursor round-trips through <see cref="ToString"/> and <see cref="TryParse"/>. It carries a digest of the
/// query's filters, so a cursor read under one sort, filter, or search is refused by another (see
/// <see cref="Fits"/>) instead of resuming at a position that means nothing there.
/// </remarks>
[PublicAPI]
public sealed record JobRunCursor
{
    private const string _FormatVersion = "r2";
    private const char _Separator = ':';
    private const char _PositionSeparator = '.';
    private const string _NoPosition = "-";
    private const string _Ascending = "a";
    private const string _Descending = "d";

    /// <summary>Gets the sort key the list was read with.</summary>
    public JobRunSortKey SortBy { get; init; }

    /// <summary>Gets the direction the list was read in.</summary>
    public ListSortDirection SortDirection { get; init; }

    /// <summary>Gets the digest of the filters the list was read with, empty for a cursor made by hand.</summary>
    public string Filter { get; init; } = "";

    /// <summary>Gets the last time job the list returned, or <see langword="null"/> before the first.</summary>
    public JobRunPosition? TimeJobs { get; init; }

    /// <summary>Gets the last cron occurrence the list returned, or <see langword="null"/> before the first.</summary>
    public JobRunPosition? CronJobOccurrences { get; init; }

    /// <summary>Returns the cursor before the first run of <paramref name="query"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    public static JobRunCursor Start(JobRunQuery query)
    {
        Argument.IsNotNull(query);

        return new JobRunCursor
        {
            SortBy = query.SortBy,
            SortDirection = query.SortDirection,
            Filter = _FilterOf(query),
        };
    }

    /// <summary>
    /// Returns whether this cursor was read with the sort, direction, and filters of <paramref name="query"/>, so its
    /// positions mean the same in it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    public bool Fits(JobRunQuery query)
    {
        Argument.IsNotNull(query);

        return SortBy == query.SortBy
            && SortDirection == query.SortDirection
            && string.Equals(Filter, _FilterOf(query), StringComparison.Ordinal);
    }

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
        return string.Join(
            _Separator,
            _FormatVersion,
            ((int)SortBy).ToString(CultureInfo.InvariantCulture),
            SortDirection == ListSortDirection.Descending ? _Descending : _Ascending,
            Filter,
            _Format(TimeJobs),
            _Format(CronJobOccurrences)
        );
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
            parts is not { Length: 6 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var sortBy)
            || !Enum.IsDefined((JobRunSortKey)sortBy)
            || !_TryParseDirection(parts[2], out var direction)
            || !_TryParsePosition(parts[4], out var timeJobs)
            || !_TryParsePosition(parts[5], out var occurrences)
        )
        {
            return false;
        }

        cursor = new JobRunCursor
        {
            SortBy = (JobRunSortKey)sortBy,
            SortDirection = direction,
            Filter = parts[3],
            TimeJobs = timeJobs,
            CronJobOccurrences = occurrences,
        };

        return true;
    }

    // A digest rather than the filters themselves keeps the cursor short and the search text out of it. Every value
    // is written in an invariant, order-free form, so each node of a cluster digests one query alike.
    private static string _FilterOf(JobRunQuery query)
    {
        var builder = new StringBuilder();

        void add(string? value)
        {
            // A length prefix, so no value can run into the next.
            builder
                .Append(value?.Length.ToString(CultureInfo.InvariantCulture) ?? "n")
                .Append('|')
                .Append(value)
                .Append('|');
        }

        add(query.Kind?.ToString());
        add(
            query.Functions is { Count: > 0 } functions
                ? string.Join(
                    ',',
                    functions
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .Select(static f => f.Length.ToString(CultureInfo.InvariantCulture) + "#" + f)
                )
                : null
        );
        add(
            query.Statuses is { Count: > 0 } statuses
                ? string.Join(
                    ',',
                    statuses.Distinct().Order().Select(static s => ((int)s).ToString(CultureInfo.InvariantCulture))
                )
                : null
        );
        add(query.ExecutionFrom?.Ticks.ToString(CultureInfo.InvariantCulture));
        add(query.ExecutionBefore?.Ticks.ToString(CultureInfo.InvariantCulture));
        add(query.CorrelationId);
        add(query.CronJobId?.ToString("N"));
        add(query.FailureFingerprint);
        add(query.ActionableFailuresOnly ? "1" : null);
        add(string.IsNullOrEmpty(query.Text) ? null : query.Text);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexStringLower(digest, 0, 8);
    }

    private static bool _TryParseDirection(string value, out ListSortDirection direction)
    {
        direction = ListSortDirection.Ascending;

        if (string.Equals(value, _Ascending, StringComparison.Ordinal))
        {
            return true;
        }

        direction = ListSortDirection.Descending;

        return string.Equals(value, _Descending, StringComparison.Ordinal);
    }

    // A function name may hold any character, so it travels base64url-encoded, whose alphabet has no separator.
    private static string _Format(JobRunPosition? position)
    {
        return position is { } value
            ? string.Join(
                _PositionSeparator,
                value.ExecutionTime.Ticks.ToString(CultureInfo.InvariantCulture),
                value.Id.ToString("N"),
                ((int)value.Status).ToString(CultureInfo.InvariantCulture),
                Base64Url.EncodeToString(Encoding.UTF8.GetBytes(value.Function))
            )
            : _NoPosition;
    }

    private static bool _TryParsePosition(string value, out JobRunPosition? position)
    {
        position = null;

        if (string.Equals(value, _NoPosition, StringComparison.Ordinal))
        {
            return true;
        }

        var parts = value.Split(_PositionSeparator);

        if (
            parts is not { Length: 4 }
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks
            || !Guid.TryParseExact(parts[1], "N", out var id)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var status)
            || !Enum.IsDefined((JobStatus)status)
            || !_TryDecode(parts[3], out var function)
        )
        {
            return false;
        }

        position = new JobRunPosition(new DateTime(ticks, DateTimeKind.Utc), function, (JobStatus)status, id);

        return true;
    }

    private static bool _TryDecode(string value, [NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!Base64Url.IsValid(value))
        {
            return false;
        }

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(
                Base64Url.DecodeFromChars(value)
            );

            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

/// <summary>One page of a run list and the cursor of the next page.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobRunPage
{
    /// <summary>Gets the runs, in the query's order.</summary>
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
