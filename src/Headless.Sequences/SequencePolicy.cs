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
}
