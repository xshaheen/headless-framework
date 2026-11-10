// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Jobs;

/// <summary>A command an Operation applies to every run in its scope.</summary>
[PublicAPI]
#pragma warning disable CA1008 // Every Operation applies one command, so a zero member would name none.
public enum JobOperationKind
{
    /// <summary>Requeue failed runs.</summary>
    Requeue = 1,

    /// <summary>Request cancellation of queued and running time jobs.</summary>
    Cancel = 2,

    /// <summary>Delete runs that are not running, with their attempts.</summary>
    Delete = 3,
}
#pragma warning restore CA1008

/// <summary>
/// The scope of an Operation, frozen when the operator confirmed it: the runs its query matches that the command may
/// apply to and that existed, unchanged, at that moment.
/// </summary>
/// <remarks>
/// <para>
/// Requeue and delete freeze on the change marker: a run is in scope when its marker is at or before
/// <see cref="HighWaterMark"/>, and each write is a compare-and-set on the marker the scope read returned. A run
/// requeued by one chunk that fails again carries a later marker, so it falls out of scope, and a chunk sent twice
/// changes nothing the second time.
/// </para>
/// <para>
/// Cancel freezes on creation: a run is in scope when it was created at or before <see cref="ConfirmedAt"/>. A running
/// run keeps advancing its marker as it reports progress, so a marker freeze would drop exactly the runs an operator
/// asked to stop; a cancel request is idempotent, so it needs no compare-and-set.
/// </para>
/// <para>This record grows by <see langword="init"/> members.</para>
/// </remarks>
[PublicAPI]
public sealed record JobOperationScope
{
    /// <summary>Gets the command the Operation applies.</summary>
    public required JobOperationKind Kind { get; init; }

    /// <summary>Gets the runs the Operation covers.</summary>
    public required JobRunQuery Query { get; init; }

    /// <summary>
    /// Gets the store's latest change marker when the Operation was confirmed, the requeue and delete freeze.
    /// </summary>
    public required DateTime HighWaterMark { get; init; }

    /// <summary>Gets when the Operation was confirmed, the cancel freeze, compared with each run's creation time.</summary>
    public required DateTimeOffset ConfirmedAt { get; init; }
}

/// <summary>One run in an Operation's scope, with the change marker its compare-and-set write expects.</summary>
/// <param name="Kind">The run's kind.</param>
/// <param name="Id">The run's identifier.</param>
/// <param name="ChangedAt">The run's change marker when the scope read returned it.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobOperationTarget(JobRunKind Kind, Guid Id, DateTime ChangedAt);

/// <summary>
/// Where an Operation's scope read resumes: time jobs first, then cron occurrences, each in identifier order.
/// </summary>
/// <param name="Kind">The table the last returned run lives in.</param>
/// <param name="AfterId">The last returned run's identifier.</param>
/// <remarks>
/// A run keeps its identifier through every write, so a scope read never repeats or skips a run because a chunk changed
/// it. Stores order identifiers differently, so a cursor is meaningful only to the store that produced it. The cursor
/// round-trips through <see cref="ToString"/> and <see cref="TryParse"/>.
/// </remarks>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct JobOperationCursor(JobRunKind Kind, Guid AfterId)
{
    private const string _FormatVersion = "o1";

    /// <summary>Formats the cursor as a string <see cref="TryParse"/> reads back.</summary>
    public override string ToString()
    {
        return string.Join(':', _FormatVersion, Kind == JobRunKind.TimeJob ? "t" : "o", AfterId.ToString("N"));
    }

    /// <summary>Reads a cursor <see cref="ToString"/> formatted.</summary>
    /// <param name="value">The formatted cursor.</param>
    /// <param name="cursor">The cursor, or the default value when <paramref name="value"/> is not one.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a cursor.</returns>
    public static bool TryParse(string? value, out JobOperationCursor cursor)
    {
        cursor = default;
        var parts = value?.Split(':');

        if (
            parts is not { Length: 3 }
            || !string.Equals(parts[0], _FormatVersion, StringComparison.Ordinal)
            || !Guid.TryParseExact(parts[2], "N", out var id)
        )
        {
            return false;
        }

        JobRunKind? kind = parts[1] switch
        {
            "t" => JobRunKind.TimeJob,
            "o" => JobRunKind.CronJobOccurrence,
            _ => null,
        };

        if (kind is not { } parsed)
        {
            return false;
        }

        cursor = new JobOperationCursor(parsed, id);

        return true;
    }
}

/// <summary>One chunk of an Operation's scope and the cursor of the next chunk.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobOperationChunk
{
    /// <summary>Gets the runs of this chunk.</summary>
    public required IReadOnlyList<JobOperationTarget> Targets { get; init; }

    /// <summary>Gets the cursor of the next chunk, or <see langword="null"/> when this chunk is the last.</summary>
    public JobOperationCursor? Next { get; init; }
}
