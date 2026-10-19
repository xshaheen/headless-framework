// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Stack;

/// <summary>
/// One messaging host of a chaos run: a real <see cref="IHost"/>, so every hosted service runs (the bootstrapper, the
/// coordination heartbeat, dead-owner recovery) and a stop drains the way a process shutdown does.
/// </summary>
public sealed class ChaosHost : IAsyncDisposable
{
    private static readonly TimeSpan _StopTimeout = TimeSpan.FromSeconds(60);

    private IHost? _host;

    internal ChaosHost(HostBehavior behavior, IHost host)
    {
        Behavior = behavior;
        _host = host;
    }

    public string Name => Behavior.Name;

    public HostBehavior Behavior { get; }

    public bool IsRunning => _host is not null;

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException($"Host {Name} has stopped.");

    public IQueue Queue => Services.GetRequiredService<IQueue>();

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        var host = _host ?? throw new InvalidOperationException($"Host {Name} has stopped.");

        // The bootstrapper runs in the background, so a started host can still be initializing: applying the schema
        // first and then joining the bootstrap makes the host ready to publish and consume when this returns.
        await host.Services.ApplyMessagingSchemaAsync(cancellationToken).ConfigureAwait(false);
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        await host.Services.GetRequiredService<IBootstrapper>().BootstrapAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Stops the host the way a process shutdown does: messaging drains within its budget, then cancels.</summary>
    public async Task StopAsync()
    {
        if (_host is not { } host)
        {
            return;
        }

        _host = null;

        try
        {
            using var timeout = new CancellationTokenSource(_StopTimeout);
            await host.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
