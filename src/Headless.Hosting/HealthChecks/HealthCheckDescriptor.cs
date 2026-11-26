// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>Describes one health check registration.</summary>
[PublicAPI]
public sealed record HealthCheckDescriptor
{
    /// <summary>Gets the registration name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the registration's tags, in ordinal order.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Gets the status the check reports when it fails.</summary>
    public required HealthStatus FailureStatus { get; init; }

    /// <summary>Gets the registration's timeout, or <see langword="null"/> when it has none.</summary>
    public required TimeSpan? Timeout { get; init; }

    /// <summary>Gets whether the registration carries the monitor's alive tag.</summary>
    public required bool IsLiveness { get; init; }
}
