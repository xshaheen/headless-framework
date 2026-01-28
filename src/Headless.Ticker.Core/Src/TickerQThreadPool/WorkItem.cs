using Headless.Checks;

namespace Headless.Ticker.TickerQThreadPool;

/// <summary>
/// Simple work item structure for the scheduler
/// </summary>
public readonly struct WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken) : IEquatable<WorkItem>
{
    public Func<CancellationToken, Task> Work { get; } = Argument.IsNotNull(work);

    public CancellationToken UserToken { get; } = userToken;

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
