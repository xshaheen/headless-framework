// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>
/// How far ahead of its <c>ExecutionTime</c> a time job counts as due. The scheduler wakes on timers and node clocks
/// that drift from the store's by fractions of a second; without this allowance a wake that lands a few milliseconds
/// early would find nothing to run and sleep again. A job may therefore start up to this much before its scheduled
/// instant, and the fallback sweep, which only takes rows overdue by the same amount, never overlaps a direct claim.
/// </summary>
internal static class JobsDueSlack
{
    /// <summary>The allowance in whole seconds, the unit every store renders into its claim statement.</summary>
    public const int Seconds = 1;

    /// <summary>The allowance as a duration, for comparisons against an app clock.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(Seconds);
}
