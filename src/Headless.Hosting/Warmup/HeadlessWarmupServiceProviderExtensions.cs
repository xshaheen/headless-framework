// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting;

/// <summary>Waits for a started host's warm-ups, for a test or a tool that drives the host itself.</summary>
[PublicAPI]
public static class HeadlessWarmupServiceProviderExtensions
{
    /// <summary>
    /// Waits until every <see cref="IWarmup" /> that gates readiness has finished, the moment the <c>warmup</c>
    /// readiness check turns <c>Healthy</c>. Completes at once when the host registers no warm-up.
    /// </summary>
    /// <param name="services">A started host's services, such as <c>host.Services</c>.</param>
    /// <param name="cancellationToken">Abandons the wait. A failed warm-up is retried until the host stops, so bound the wait.</param>
    /// <returns>A task that completes when the host is warm.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken" /> was canceled first; the message names each pending warm-up and its last error.
    /// </exception>
    public static async Task WaitForWarmupAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(services);

        if (services.GetService<WarmupRunner>() is not { } runner)
        {
            return;
        }

        try
        {
            await runner.WaitForInitializationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"The host did not finish warming up: {runner.DescribePending() ?? "no warm-up is pending"}.",
                exception,
                cancellationToken
            );
        }
    }
}
