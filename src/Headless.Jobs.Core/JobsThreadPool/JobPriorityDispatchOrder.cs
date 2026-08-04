// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.JobsThreadPool;

/// <summary>
/// Canonical High → Normal → Low → LongRunning dispatch rank. <see cref="JobPriority"/> enum values are
/// wire-stable contract metadata (<see cref="JobPriority.Normal"/> = 0 is the attribute default), so they do
/// not encode dispatch order — sorting by the raw enum dispatches Normal before High. Every dispatch-order
/// sort must go through this rank.
/// </summary>
internal static class JobPriorityDispatchOrder
{
    public static int DispatchRank(this JobPriority priority)
    {
        return priority switch
        {
            JobPriority.High => 0,
            JobPriority.Normal => 1,
            JobPriority.Low => 2,
            // LongRunning admissions park on the detached lane and do not compete for worker slots; order
            // them after slot-competing work so a co-due batch fills lanes in slot-pressure order.
            _ => 3,
        };
    }
}
