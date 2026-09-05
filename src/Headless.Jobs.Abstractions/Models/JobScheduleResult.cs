// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

/// <summary>Observed current run and generation; cancellation requests do not prove external effects stopped.</summary>
[PublicAPI]
public sealed record JobScheduleResult(
    JobScheduleDisposition Disposition,
    Guid? RunId,
    long? Generation,
    JobStatus? State
);
