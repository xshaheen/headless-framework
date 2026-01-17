namespace Framework.Ticker.TickerQThreadPool;

/// <summary>
/// Simple work item structure for the scheduler
/// </summary>
public readonly struct WorkItem(Func<CancellationToken, Task> work, CancellationToken userToken)
{
    public Func<CancellationToken, Task> Work { get; } = work ?? throw new ArgumentNullException(nameof(work));

    public CancellationToken UserToken { get; } = userToken;
}
