// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>Observed current run and generation; cancellation requests do not prove external effects stopped.</summary>
[PublicAPI]
public sealed record JobScheduleResult(
    JobScheduleDisposition Disposition,
    Guid? RunId,
    long? Generation,
    JobStatus? State
)
{
    /// <summary>The observation/write belongs to the caller transaction; its durable effect depends on the outer commit.</summary>
    public bool IsProvisional { get; init; }
}
