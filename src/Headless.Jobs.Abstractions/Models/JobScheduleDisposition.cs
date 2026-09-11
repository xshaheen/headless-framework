// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Models;

/// <summary>The outcome of one keyed operation, independently of the observed execution state.</summary>
[PublicAPI]
public enum JobScheduleDisposition
{
    Created,
    Existing,
    Conflict,
    Replaced,
    NotFound,
    StaleGeneration,
    Cancelled,
    CancellationRequested,
    Terminal,
}
