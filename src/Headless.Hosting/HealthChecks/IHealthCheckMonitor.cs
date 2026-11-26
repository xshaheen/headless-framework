// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>Lists the host's health checks, probes them on demand, and keeps each check's recent probe results.</summary>
/// <remarks>
/// <see cref="GetChecks"/> and <see cref="GetSnapshot"/> read memory only, so display code calls them freely.
/// <see cref="ProbeAllAsync"/> and <see cref="ProbeAsync"/> run the real checks against their dependencies: treat
/// them as operator actions, not as a refresh. History fills only from these two methods; a request to a health
/// endpoint or the <c>check</c> command runs the checks without recording anything here.
/// </remarks>
[PublicAPI]
public interface IHealthCheckMonitor
{
    /// <summary>Lists every health check registration the host has now, after any removal.</summary>
    /// <returns>One descriptor per registration.</returns>
    IReadOnlyList<HealthCheckDescriptor> GetChecks();

    /// <summary>Runs every registered check and records one result per check.</summary>
    /// <param name="cancellationToken">
    /// Stops this caller's wait. Concurrent callers share one run, which is cancelled, and records nothing, only when
    /// every waiting caller has cancelled.
    /// </param>
    /// <returns>The report of the run.</returns>
    /// <exception cref="ObjectDisposedException">The monitor was disposed.</exception>
    Task<HealthReport> ProbeAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs the check registered under <paramref name="name"/>, matched case-insensitively, and records its result.</summary>
    /// <param name="name">The registration name.</param>
    /// <param name="cancellationToken">Cancels the run; a cancelled run records nothing.</param>
    /// <returns>The report of the run, or <see langword="null"/> when no registration has <paramref name="name"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">The monitor was disposed.</exception>
    Task<HealthReport?> ProbeAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns each current registration with its last 30 recorded results, and the overall status.</summary>
    /// <returns>The snapshot. History of a registration that no longer exists is not included.</returns>
    HealthCheckSnapshot GetSnapshot();
}
