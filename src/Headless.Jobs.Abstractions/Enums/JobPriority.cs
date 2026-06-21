// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Enums;

/// <summary>
/// Controls which thread pool and execution order the Jobs scheduler uses for a job function.
/// </summary>
public enum JobPriority
{
    /// <summary>
    /// Runs the job on a dedicated long-running thread using the default <c>TaskScheduler</c>.
    /// Use for CPU-bound or blocking work that would monopolize a shared worker thread.
    /// </summary>
    LongRunning,

    /// <summary>
    /// Dispatched to the Jobs thread pool ahead of <see cref="Normal"/> and <see cref="Low"/> work.
    /// </summary>
    High,

    /// <summary>
    /// Standard priority dispatched to the Jobs thread pool after <see cref="High"/> work. This is the
    /// default for functions declared with <c>JobFunctionAttribute</c>.
    /// </summary>
    Normal,

    /// <summary>
    /// Dispatched to the Jobs thread pool last, after <see cref="High"/> and <see cref="Normal"/> work.
    /// </summary>
    Low,
}
