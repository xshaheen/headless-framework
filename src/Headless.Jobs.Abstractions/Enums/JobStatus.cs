namespace Headless.Jobs.Enums;

public enum JobStatus
{
    Idle,
    Queued,
    InProgress,
    Succeeded,
    DueDone,
    Failed,
    Cancelled,
    Skipped,
}
