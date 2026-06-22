// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Controls when the Jobs scheduler begins processing jobs after the host starts.
/// </summary>
public enum JobsStartMode
{
    /// <summary>
    /// Job processing begins automatically when the host starts. This is the default.
    /// </summary>
    Immediate,

    /// <summary>
    /// Background services are registered but the scheduler does not run its first iteration
    /// automatically. Call <c>IJobsHostScheduler.StartAsync</c> to begin processing.
    /// Use this mode when job processing should be deferred until after additional application
    /// warm-up work completes.
    /// </summary>
    Manual,
}
