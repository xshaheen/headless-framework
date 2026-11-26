// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>Resolves the self-fence margin, so the fence and every report of it read one value.</summary>
internal static class SelfFenceMargin
{
    private static readonly TimeSpan _MaxDefault = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The margin the fence applies: the configured value, else the smaller of 5 seconds and a sixth of
    /// <see cref="CoordinationOptions.DeadThreshold"/>. A sixth leaves five sixths of the threshold, which the
    /// DeadThreshold &gt;= 3 x HeartbeatInterval rule already makes at least 2.5 beats, so the default never violates
    /// the two-beat rule a fixed 5 seconds breaks on any threshold under 5 seconds plus two beats.
    /// </summary>
    public static TimeSpan Resolve(CoordinationOptions options)
    {
        if (options.SelfFenceMargin is { } configured)
        {
            return configured;
        }

        var fraction = TimeSpan.FromTicks(options.DeadThreshold.Ticks / 6);
        return fraction.Min(_MaxDefault);
    }
}
