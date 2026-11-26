// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>Represents the recorded health of every current registration.</summary>
/// <param name="Status">
/// The worst latest status among registrations with at least one result, or <see langword="null"/> when none has one.
/// </param>
/// <param name="Checks">One entry per current registration.</param>
[PublicAPI]
public sealed record HealthCheckSnapshot(HealthStatus? Status, IReadOnlyList<HealthCheckHistory> Checks);
