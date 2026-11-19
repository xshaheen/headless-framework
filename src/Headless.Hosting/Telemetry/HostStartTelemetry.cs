// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;

namespace Headless.Hosting;

/// <summary>Records the time from process start until the host raises <see cref="IHostApplicationLifetime.ApplicationStarted" />.</summary>
/// <remarks>
/// Registered with the first startup validator or warm-up. The lifetime is optional so a bare service provider that
/// enumerates the hosted services, as a command or a test does, can still construct it.
/// </remarks>
internal sealed class HostStartTelemetry(IHostApplicationLifetime? lifetime = null) : IHostedService, IDisposable
{
    private CancellationTokenRegistration _started;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The lifetime raises ApplicationStarted once, so a restarted host has nothing more to record.
        if (lifetime is not null && _started == default)
        {
            _started = lifetime.ApplicationStarted.Register(static () => HostingTelemetry.HostStarted());
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _started.Dispose();
}
