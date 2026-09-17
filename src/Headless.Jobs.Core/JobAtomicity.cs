// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.UnitOfWork;

namespace Headless.Jobs;

internal static class JobAtomicity
{
    // Strictest-wins tree walk: TransactionEnlistment.Required anywhere in the tree forces the whole tree atomic
    // (mirrors the pre-existing "any child forces the whole tree" rule), regardless of what siblings request.
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
            if (job.Enlistment == TransactionEnlistment.Required)
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
        RejectDirect(IsRequired(jobs));
    }

    internal static void RejectDirect(bool anyRequiresEnlistment)
    {
        if (anyRequiresEnlistment)
        {
            throw new InvalidOperationException(
                "TransactionEnlistment.Required Jobs scheduling needs a compatible live relational unit of work and the coordinated manager/writer path; direct persistence cannot satisfy it."
            );
        }
    }
}
