// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;

namespace Headless.Jobs;

internal static class JobAtomicity
{
    internal static bool IsRequired<TJob>(IEnumerable<TJob> jobs)
        where TJob : TimeJobEntity<TJob>
    {
        var pending = new Stack<TJob>(jobs);
        var visited = new HashSet<TJob>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var job))
        {
            if (!visited.Add(job))
            {
                continue;
            }
            if (job.RequireAtomicEnlistment)
            {
                return true;
            }
            foreach (var child in job.Children)
            {
                pending.Push(child);
            }
        }
        return false;
    }

    internal static void RejectDirect<TJob>(IEnumerable<TJob> jobs)
        where TJob : TimeJobEntity<TJob>
    {
        if (IsRequired(jobs))
        {
            throw new InvalidOperationException(
                "Required atomic Jobs scheduling needs a compatible live relational transaction and the coordinated manager/writer path; direct persistence cannot satisfy it."
            );
        }
    }
}
