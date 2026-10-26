// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>
/// How far ahead of its <c>ExecutionTime</c> a time job counts as due when a store claims it. The scheduler claims
/// once the earliest row of a one-second window is due by the store clock; this allowance lets that one claim take the
/// rest of the window instead of refusing rows a fraction of a second later. A job may therefore start up to this much
/// before its scheduled instant. The scheduler's own gate applies no allowance, so a wake that lands a few
/// milliseconds early polls once more rather than claiming.
/// </summary>
internal static class JobsDueSlack
{
    public const int Seconds = 1;

    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(Seconds);
}
