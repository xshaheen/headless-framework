// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>What the startup cost check does with an out-of-range measurement.</summary>
[PublicAPI]
public enum SecretHasherCostCheckMode
{
    /// <summary>Skip the benchmark entirely.</summary>
    Off = 0,

    /// <summary>
    /// Benchmark in the background once the host has started, and log a warning for an out-of-range result. Startup
    /// never waits for it.
    /// </summary>
    Warn = 1,

    /// <summary>
    /// Benchmark before any hosted service starts, and fail startup for an out-of-range result. Every instance waits
    /// for the benchmark, and a busy node can fail a start with sound parameters, so prefer it for a staging or CI check.
    /// </summary>
    Strict = 2,
}
