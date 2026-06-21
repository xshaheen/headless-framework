// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
