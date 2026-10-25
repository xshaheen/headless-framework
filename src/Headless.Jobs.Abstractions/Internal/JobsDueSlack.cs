// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>
/// How far ahead of its <c>ExecutionTime</c> a time job counts as due. The scheduler wakes on timers and node clocks
/// that drift from the store's by fractions of a second; without this allowance a wake that lands a few milliseconds
/// early would find nothing to run and sleep again. A job may therefore start up to this much before its scheduled
/// instant.
/// </summary>
internal static class JobsDueSlack
{
    public const int Seconds = 1;

    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(Seconds);
}
