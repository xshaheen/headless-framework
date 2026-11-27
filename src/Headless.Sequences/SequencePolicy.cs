// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>Defines how one counter name numbers: its start value, its step, and the entry point that serves it.</summary>
/// <remarks>
/// <see cref="Start" /> is read only when a key's row is created, so changing it never moves an existing counter.
/// A changed <see cref="Step" /> applies from the next call, and the existing counter then mixes both steps.
/// </remarks>
[PublicAPI]
public sealed record SequencePolicy
{
    /// <summary>Gets the initial value allocated by a new counter. The default is 1.</summary>
    public long Start { get; init; } = 1;

    /// <summary>Gets the distance between two consecutive values; greater than 0. The default is 1.</summary>
    public long Step { get; init; } = 1;

    /// <summary>
    /// Gets the entry point that serves the counter: <see cref="SequenceMode.Fast" /> through
    /// <see cref="ISequenceGenerator" />, <see cref="SequenceMode.GapFree" /> through <c>unit.Sequences.NextAsync</c>,
    /// or <see cref="SequenceMode.Reported" /> through <c>unit.Sequences.AdvanceToAsync</c>. The default is
    /// <see cref="SequenceMode.Fast" />.
    /// </summary>
    public SequenceMode Mode { get; init; } = SequenceMode.Fast;

    /// <summary>
    /// Gets how many values a fast-mode call reserves from the database at once; at least 1. The default is 1: every
    /// value is its own database call, and values come out in call order across every host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Above 1, <see cref="ISequenceGenerator.NextAsync" /> and <see cref="ISequenceGenerator.NextNumberAsync" /> reserve
    /// a block of this many values in one database call and hand them out from memory, one block per counter key on
    /// each host. Every value is still issued once, but numbering is no longer monotonic across hosts: two hosts hand out
    /// values from their own blocks, so their numbers interleave. Unused values of a block are lost, as gaps, when the
    /// process stops, when the key is evicted (see <see cref="SequencesOptions.MaxCachedBlocks" />), when the policy's
    /// step or block size changes, and when a document-number period ends. A block never spans a reset: each period is
    /// its own key.
    /// </para>
    /// <para>
    /// Only <see cref="SequenceMode.Fast" /> accepts a value above 1. A gap-free or reported policy with one fails options
    /// validation at startup. <see cref="ISequenceGenerator.ReserveAsync" /> always reserves its own range directly.
    /// </para>
    /// </remarks>
    public int BlockSize { get; init; } = 1;

    /// <summary>
    /// Gets the template a document number is formatted with, or <see langword="null" /> to format the bare value.
    /// </summary>
    /// <remarks>
    /// Tokens in braces are replaced; everything else is literal, and <c>{{</c> and <c>}}</c> write a brace.
    /// <list type="bullet">
    /// <item><description><c>{seq}</c> the value; <c>{seq:D6}</c> pads it with zeros to six digits (any width from 1 to 19).</description></item>
    /// <item><description><c>{yyyy}</c>, <c>{yy}</c>, <c>{MM}</c>, <c>{dd}</c> the issue date in <see cref="TimeZone" />.</description></item>
    /// <item><description><c>{fy}</c> the year the fiscal year starts in (see <see cref="FiscalYearStartMonth" />).</description></item>
    /// </list>
    /// For example <c>REC-{yyyy}-{seq:D6}</c> formats <c>REC-2026-000042</c>. An unknown token fails options
    /// validation at startup.
    /// </remarks>
    public string? Format { get; init; }

    /// <summary>
    /// Gets when a document-number counter starts again from <see cref="Start" />: never (the default), or every
    /// year, month, day, or fiscal year, in <see cref="TimeZone" />. Each period is its own counter partition.
    /// </summary>
    public SequenceReset Reset { get; init; } = SequenceReset.Never;

    /// <summary>
    /// Gets the month, 1 to 12, the fiscal year starts in, used by <see cref="SequenceReset.FiscalYear" /> and the
    /// <c>{fy}</c> token. The default is 1 (January), so the fiscal year matches the calendar year.
    /// </summary>
    public int FiscalYearStartMonth { get; init; } = 1;

    /// <summary>
    /// Gets the time zone a document number's date and reset period are decided in, so a counter that resets daily
    /// starts again at the business's local midnight. The default is UTC.
    /// </summary>
    /// <remarks>The date comes from the registered <see cref="TimeProvider" />, converted into this zone.</remarks>
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Returns the counter partition a document number taken at <paramref name="instant" /> goes to, such as
    /// <c>2026</c>, <c>2026-10</c>, <c>2026-10-08</c>, or <c>FY2026</c>, decided in <see cref="TimeZone" />.
    /// </summary>
    /// <param name="instant">The instant the number would be taken at.</param>
    /// <returns>The partition, or <see langword="null" /> when <see cref="Reset" /> is <see cref="SequenceReset.Never" />.</returns>
    public string? PartitionAt(DateTimeOffset instant)
    {
        return SequenceNumberFormat.Partition(this, SequenceNumberFormat.IssuedOn(this, instant));
    }

    /// <summary>
    /// Returns the first instant after <paramref name="instant" /> that belongs to the next reset period: local
    /// midnight in <see cref="TimeZone" /> of the next day, the first of the next month, January 1, or the first day
    /// of the next fiscal year.
    /// </summary>
    /// <remarks>
    /// When a daylight-saving jump skips that local midnight, the reset is the first instant of the new local date.
    /// When the clock falls back over it, the reset is the earlier of the two instants.
    /// </remarks>
    /// <param name="instant">The instant to look forward from.</param>
    /// <returns>The reset instant in UTC, or <see langword="null" /> when <see cref="Reset" /> is <see cref="SequenceReset.Never" />.</returns>
    public DateTimeOffset? NextResetAt(DateTimeOffset instant)
    {
        return SequenceNumberFormat.NextResetAt(this, instant);
    }
}
