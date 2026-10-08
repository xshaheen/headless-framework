// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>When a document-number counter starts again from its start value.</summary>
[PublicAPI]
public enum SequenceReset
{
    /// <summary>Never: one counter for all time.</summary>
    Never = 0,

    /// <summary>Every calendar year; the partition is the year, such as <c>2026</c>.</summary>
    Year = 1,

    /// <summary>Every month; the partition is the year and month, such as <c>2026-10</c>.</summary>
    Month = 2,

    /// <summary>Every day; the partition is the date, such as <c>2026-10-08</c>.</summary>
    Day = 3,

    /// <summary>
    /// Every fiscal year, starting in the policy's fiscal-year start month; the partition is <c>FY</c> and the year it
    /// starts in, such as <c>FY2026</c>.
    /// </summary>
    FiscalYear = 4,
}
