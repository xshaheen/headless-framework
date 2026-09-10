// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Headless.Hosting.DependencyInjection;

/// <summary>
/// Fails the host at startup when a requirement declared through
/// <c>IServiceCollection.RequireRegisteredService&lt;T&gt;(…)</c> has no registration behind it.
/// </summary>
/// <remarks>
/// Registered as an <see cref="IHostedService"/> and implemented as an <see cref="IHostedLifecycleService"/>
/// so the check runs in <see cref="StartingAsync"/>, before any hosted service's
/// <see cref="IHostedService.StartAsync"/>. Otherwise background workers and message consumers would start
/// under an assumption the container cannot honour, and the first symptom would be a resolve failure on a
/// live request instead of a refused start.
/// </remarks>
internal sealed class RequiredServiceStartupValidator(RequiredServiceRegistry registry, IServiceProvider services)
    : IHostedLifecycleService
{
    /// <inheritdoc/>
    /// <exception cref="MissingRequiredServiceException">One or more declared requirements are unregistered.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Synchronous by design: a throw here surfaces to the host the same way a faulted task would, and
        // carries the typed exception with every unsatisfied requirement.
        _Validate();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void _Validate()
    {
        // Probe, never resolve. Resolving would construct the very service under test — for a Redis-backed
        // cache that reaches into connection options during startup — turning a provider misconfiguration
        // into an opaque failure attributed to this guard instead of the actionable message below.
        var probe = services.GetService<IServiceProviderIsService>();

        var missing = registry
            .Registrations.Where(registration => !_IsRegistered(probe, registration.ServiceType))
            .ToArray();

        if (missing.Length == 0)
        {
            return;
        }

        var lines = missing.Select(registration =>
            $"  - {registration.ServiceType.GetFriendlyTypeName()} (required by {registration.RequiredBy}): {registration.Remedy}"
        );

        var message =
            $"Headless startup validation failed: {missing.Length} required service registration(s) are missing."
            + Environment.NewLine
            + string.Join(Environment.NewLine, lines);

        throw new MissingRequiredServiceException(message, missing);
    }

    /// <summary>
    /// Reports whether <paramref name="serviceType"/> would resolve, preferring
    /// <see cref="IServiceProviderIsService"/> over an actual resolve. The probe answers a constructed
    /// generic (<c>ICache&lt;Foo&gt;</c>) from an open-generic registration (<c>typeof(ICache&lt;&gt;)</c>),
    /// which is exactly how the caching providers register their per-item caches. Falls back to a
    /// null-returning resolve for a container that does not expose the probe.
    /// </summary>
    private bool _IsRegistered(IServiceProviderIsService? probe, Type serviceType)
    {
        return probe is not null ? probe.IsService(serviceType) : services.GetService(serviceType) is not null;
    }
}
