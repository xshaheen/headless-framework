// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>Describes one health check registration.</summary>
/// <param name="Name">The registration name.</param>
/// <param name="Tags">The registration's tags, in ordinal order.</param>
/// <param name="FailureStatus">The status the check reports when it fails.</param>
/// <param name="Timeout">The registration's timeout, or <see langword="null"/> when it has none.</param>
/// <param name="IsLiveness">Whether the registration carries the monitor's alive tag.</param>
[PublicAPI]
public sealed record HealthCheckDescriptor(
    string Name,
    IReadOnlyList<string> Tags,
    HealthStatus FailureStatus,
    TimeSpan? Timeout,
    bool IsLiveness
);
