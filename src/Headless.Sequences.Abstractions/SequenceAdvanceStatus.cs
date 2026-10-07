// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sequences;

/// <summary>What happened when a reported counter was moved forward to a value.</summary>
[PublicAPI]
public enum SequenceAdvanceStatus
{
    /// <summary>The value was the next one expected (the policy's start for a new counter), and the counter moved to it.</summary>
    Next = 0,

    /// <summary>
    /// The value was past the next one expected, so values in between were never reported; the counter moved to it
    /// anyway. Treat it as a gap to alert on, or roll the unit back to refuse it.
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// The value was at or below the highest one already accepted: a replay or an out-of-order report. The counter
    /// did not change.
    /// </summary>
    Stale = 2,
}
