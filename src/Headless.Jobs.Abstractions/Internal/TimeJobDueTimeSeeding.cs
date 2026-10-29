// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Internal;

/// <summary>Applies a <see cref="TimeJobDueTimeSeeder"/> to the roots of one write, shared by every built-in store.</summary>
internal static class TimeJobDueTimeSeeding
{
    public static void Apply<TTimeJob>(IEnumerable<TTimeJob> roots, TimeJobDueTimeSeeder seeder, DateTime storeUtcNow)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        foreach (var root in roots)
        {
            if (seeder(root, storeUtcNow) is { } dueUtc)
            {
                root.ExecutionTime = dueUtc;
            }
        }
    }
}
