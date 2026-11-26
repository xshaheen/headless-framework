// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>Represents one recorded run of a health check.</summary>
[PublicAPI]
public sealed record HealthCheckProbeResult
{
    /// <summary>Gets the status the run reported.</summary>
    public required HealthStatus Status { get; init; }

    /// <summary>Gets how long the run took.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the check's description, or fixed failure text when the check threw, so an exception message never
    /// reaches the history.
    /// </summary>
    public required string? Description { get; init; }

    /// <summary>Gets when the run's report returned, from the registered <see cref="TimeProvider"/>.</summary>
    public required DateTimeOffset CompletedAt { get; init; }
}
