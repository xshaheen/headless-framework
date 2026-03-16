namespace Headless.Jobs.Enums;

public enum JobStatus
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
