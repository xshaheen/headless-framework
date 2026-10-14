// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs.JobsThreadPool;

/// <summary>
/// Simple work item structure for the scheduler
/// </summary>
internal readonly struct WorkItem(
    Func<CancellationToken, Task> work,
    CancellationToken userToken,
    SemaphoreSlim? gate = null,
    JobPriority priority = JobPriority.Normal,
    bool holdsPermit = false
) : IEquatable<WorkItem>
{
    public Func<CancellationToken, Task> Work { get; } = Argument.IsNotNull(work);

    public CancellationToken UserToken { get; } = userToken;

    /// <summary>The per-function concurrency gate the item must hold a permit of while it runs, if any.</summary>
    public SemaphoreSlim? Gate { get; } = gate;

    public JobPriority Priority { get; } = priority;

    /// <summary>Whether the permit of <see cref="Gate"/> was already acquired on this item's behalf.</summary>
    public bool HoldsPermit { get; } = holdsPermit;

    public WorkItem WithPermit()
    {
        return new WorkItem(Work, UserToken, Gate, Priority, holdsPermit: true);
    }

    public bool Equals(WorkItem other)
    {
        return ReferenceEquals(Work, other.Work) && UserToken.Equals(other.UserToken);
    }

    public override bool Equals(object? obj)
    {
        return obj is WorkItem other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Work, UserToken);
    }

    public static bool operator ==(WorkItem left, WorkItem right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(WorkItem left, WorkItem right)
    {
        return !(left == right);
    }
}
