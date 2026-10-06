// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>
/// Wraps a health check that Headless did not write, so a contributed check reports a failure the way every Headless
/// check does: the registration's failure status, fixed description text, and the exception kept for the logs.
/// </summary>
/// <remarks>
/// Third-party checks often put the driver's exception message in the description, and some report every exception as
/// <see cref="HealthStatus.Unhealthy" /> whatever the configured failure status. A health endpoint is often anonymous,
/// and a driver message can carry host names or account details.
/// </remarks>
internal sealed class HeadlessSanitizedHealthCheck(Func<IHealthCheck> createInner) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        HealthCheckResult result;

        try
        {
            // Created inside the try: resolving the check's dependencies (a DbContext, a client) can throw too.
            result = await createInner().CheckHealthAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The health check service reports its own timeout; a caller that went away gets no result.
            throw;
        }
#pragma warning disable CA1031 // A health check is a reporting boundary: every failure becomes the registration's failure status.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return HeadlessProbeHealthCheck.Failed(context, exception);
        }

        if (result.Status == HealthStatus.Healthy)
        {
            return result;
        }

        // A check that swallowed a cancellation the caller asked for must not report the dependency as down.
        if (result.Exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return HeadlessProbeHealthCheck.Failed(context, result.Exception, result.Data);
    }
}
