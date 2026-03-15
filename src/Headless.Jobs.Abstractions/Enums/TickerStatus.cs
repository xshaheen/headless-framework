namespace Headless.Jobs.Enums;

public enum TickerStatus
{
    Idle,
    Queued,
    InProgress,
    Done,
    DueDone,
    Failed,
    Cancelled,
    Skipped,
}
