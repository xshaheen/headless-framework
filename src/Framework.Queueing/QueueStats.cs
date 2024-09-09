// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics;

namespace Framework.Queueing;

[DebuggerDisplay(
    $"Queued={{{nameof(Queued)}}}, "
        + $"Working={{{nameof(Working)}}}, "
        + $"DeadLetter={{{nameof(DeadLetter)}}}, "
        + $"Enqueued={{{nameof(Enqueued)}}}, "
        + $"Dequeued={{{nameof(Dequeued)}}}, "
        + $"Completed={{{nameof(Completed)}}}, "
        + $"Abandoned={{{nameof(Abandoned)}}}, "
        + $"Errors={{{nameof(Errors)}}}, "
        + $"Timeouts={{{nameof(Timeouts)}}}"
)]
public sealed class QueueStats
{
    public required long Queued { get; init; }

    public required long Working { get; init; }

    public required long DeadLetter { get; init; }

    public required long Enqueued { get; init; }

    public required long Dequeued { get; init; }

    public required long Completed { get; init; }

    public required long Abandoned { get; init; }

    public required long Errors { get; init; }

    public required long Timeouts { get; init; }
}
