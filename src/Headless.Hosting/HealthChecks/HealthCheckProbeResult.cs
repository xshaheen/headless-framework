// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>Represents one recorded run of a health check.</summary>
/// <param name="Status">The status the run reported.</param>
/// <param name="Duration">How long the run took.</param>
/// <param name="Description">
/// The check's description, or fixed failure text when the check threw, so an exception message never reaches the
/// history.
/// </param>
/// <param name="CompletedAt">When the run's report returned, from the registered <see cref="TimeProvider"/>.</param>
[PublicAPI]
public sealed record HealthCheckProbeResult(
    HealthStatus Status,
    TimeSpan Duration,
    string? Description,
    DateTimeOffset CompletedAt
);
