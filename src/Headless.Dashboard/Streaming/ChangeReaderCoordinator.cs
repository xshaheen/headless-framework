// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Runs one change reader per module while the node has at least one live stream open: the node's first stream starts
/// them and its last close stops them, so a node nobody watches reads nothing.
/// </summary>
/// <remarks>
/// Readers read every change of their module, whatever the open streams subscribe to, and each stream filters. A
/// module refused for its SDK version gets no reader. A restart after a stop records a gap in the event ring, because
/// the store changes made while the readers were stopped never reached it.
/// </remarks>
internal sealed class ChangeReaderCoordinator(
    DashboardManifestProvider manifest,
    DashboardEventPublisher publisher,
    EventRingBuffer buffer,
    IOptions<HeadlessDashboardOptions> options,
    TimeProvider timeProvider,
    ILogger<ChangeReader> logger
) : IDisposable
{
    private readonly Lock _lock = new();
    private ChangeReader[] _readers = [];
    private int _openStreams;
    private bool _disposed;

    /// <summary>
    /// Counts one more open stream, starting the readers when it is the first. Dispose the returned lease when the
    /// stream closes; disposing it again does nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">A module's manifest is missing or invalid.</exception>
    public IDisposable Open()
    {
        lock (_lock)
        {
            if (!_disposed && _openStreams++ == 0)
            {
                try
                {
                    _Start();
                }
                catch
                {
                    // The stream fails to open, so it is not counted; the next stream tries the start again.
                    _openStreams--;
                    throw;
                }
            }
        }

        return new Lease(this);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _Stop();
        }
    }

    private void _Close()
    {
        lock (_lock)
        {
            if (_disposed || --_openStreams > 0)
            {
                return;
            }

            _openStreams = 0;
            _Stop();
        }
    }

    private void _Start()
    {
        var readers = new List<ChangeReader>();

        foreach (var module in manifest.Manifest.Modules)
        {
            if (module.IsRefused || module.Module is not IDashboardChangeSource source)
            {
                continue;
            }

            readers.Add(
                new ChangeReader(module.Id, source, publisher, options.Value.ChangePollInterval, timeProvider, logger)
            );
        }

        // Only a store read can miss a change while stopped; a module's own publishes always reach the ring.
        if (readers.Count > 0)
        {
            buffer.MarkGap();
        }

        _readers = [.. readers];
    }

    private void _Stop()
    {
        foreach (var reader in _readers)
        {
            reader.Dispose();
        }

        _readers = [];
    }

    private sealed class Lease(ChangeReaderCoordinator coordinator) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                coordinator._Close();
            }
        }
    }
}
