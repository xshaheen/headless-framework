// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Hosting;

/// <summary>Adapts a probe delegate to <see cref="IHealthCheck" />: a probe that completes is healthy.</summary>
internal sealed class HeadlessProbeHealthCheck(
    IServiceProvider services,
    Func<IServiceProvider, CancellationToken, Task> probe
) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await probe(services, cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The health check service reports its own timeout; a caller that went away gets no result.
            throw;
        }
#pragma warning disable CA1031 // A health check is a reporting boundary: every probe failure becomes the registration's failure status.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // The description is fixed text because health endpoints are often anonymous, and a driver's exception
            // message can carry host names or account details. The exception still reaches the health check logs.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"The '{context.Registration.Name}' dependency probe failed.",
                exception
            );
        }
    }
}
