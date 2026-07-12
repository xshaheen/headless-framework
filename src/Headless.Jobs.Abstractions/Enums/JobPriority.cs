// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Controls which thread pool and execution order the Jobs scheduler uses for a job function.
/// </summary>
/// <remarks>
/// New members may be added in future versions; consumers that <see langword="switch"/> on this enum
/// should include a default arm.
/// </remarks>
[PublicAPI]
public enum JobPriority
{
    /// <summary>
    /// Standard priority dispatched to the Jobs thread pool after <see cref="High"/> work. This is the
    /// default value (<c>default(JobPriority)</c>) and the default for functions declared with
    /// <c>JobFunctionAttribute</c>.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Dispatched to the Jobs thread pool ahead of <see cref="Normal"/> and <see cref="Low"/> work.
    /// </summary>
    High = 1,

    /// <summary>
    /// Dispatched to the Jobs thread pool last, after <see cref="High"/> and <see cref="Normal"/> work.
    /// </summary>
    Low = 2,

    /// <summary>
    /// Runs the job on a dedicated long-running thread using the default <c>TaskScheduler</c>.
    /// Use for CPU-bound or blocking work that would monopolize a shared worker thread.
    /// </summary>
    LongRunning = 3,
}
