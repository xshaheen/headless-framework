// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Threading.Channels;
using Headless.Primitives;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>
/// One lazy, bounded, non-blocking FIFO shared by a cache hub and its tier sub-hubs. Queue entries are structs and
/// handler snapshots are copy-on-write array references, so accepted signals add no allocation beyond their event args.
/// </summary>
internal sealed class CacheEventDispatcher : IDisposable, IAsyncDisposable
{
    private readonly string _cacheName;
    private readonly int _capacity;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly ILogger? _logger;
    private readonly Lock _initializationLock = new();

    private DispatchState? _state;
    private int _disposed;
    private long _accepted;
    private long _processed;
    private long _dropped;
    private long _pending;

    public CacheEventDispatcher(string cacheName, CacheEventsConfig config, ILogger? logger)
    {
        if (config.BufferCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.BufferCapacity,
                "The cache event buffer capacity must be greater than zero."
            );
        }

        if (config.ShutdownDrainTimeout < TimeSpan.Zero && config.ShutdownDrainTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.ShutdownDrainTimeout,
                "The cache event shutdown drain timeout must be non-negative or infinite."
            );
        }

        _cacheName = cacheName;
        _capacity = config.BufferCapacity;
        _shutdownDrainTimeout = config.ShutdownDrainTimeout;
        _logger = logger;
        HandlerErrorCallback = _CreateErrorLogger(logger, config.HandlerErrorLogLevel);
    }

    private Action<Exception> HandlerErrorCallback { get; }

    public CacheEventDispatchStatistics Statistics =>
        new(
            Accepted: Interlocked.Read(ref _accepted),
            Processed: Interlocked.Read(ref _processed),
            Dropped: Interlocked.Read(ref _dropped),
            Pending: Interlocked.Read(ref _pending),
            Capacity: _capacity
        );

    public void Dispatch<TArgs>(object handlerSnapshot, object sender, TArgs args)
        where TArgs : EventArgs
    {
        var state = Volatile.Read(ref _state) ?? _GetOrCreateState();

        if (state is null)
        {
            _RecordDropped(1);
            return;
        }

        Interlocked.Increment(ref _pending);

        if (state.Channel.Writer.TryWrite(CacheEventDispatchEntry.Create<TArgs>(handlerSnapshot, sender, args)))
        {
            Interlocked.Increment(ref _accepted);
            return;
        }

        Interlocked.Decrement(ref _pending);
        _RecordDropped(1);
    }

    public async ValueTask WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        while (Interlocked.Read(ref _pending) != 0)
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        var state = _BeginShutdown();

        if (state is null)
        {
            return;
        }

        if (!state.Worker.Wait(_shutdownDrainTimeout))
        {
            _LogDrainTimeout();
            _ = state.Cancellation.CancelAsync();
            return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var state = _BeginShutdown();

        if (state is null)
        {
            return;
        }

        try
        {
            await state.Worker.WaitAsync(_shutdownDrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _LogDrainTimeout();
            _ = state.Cancellation.CancelAsync();
        }
    }

    private DispatchState? _GetOrCreateState()
    {
        lock (_initializationLock)
        {
            if (_disposed != 0)
            {
                return null;
            }

            if (_state is not null)
            {
                return _state;
            }

            var channel = Channel.CreateBounded<CacheEventDispatchEntry>(
                new BoundedChannelOptions(_capacity)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                }
            );
            var cancellation = new CancellationTokenSource();
            var worker = _ConsumeAsync(channel.Reader, cancellation);
            var state = new DispatchState(channel, cancellation, worker);
            Volatile.Write(ref _state, state);

            return state;
        }
    }

    private DispatchState? _BeginShutdown()
    {
        DispatchState? state;

        lock (_initializationLock)
        {
            if (_disposed != 0)
            {
                return null;
            }

            _disposed = 1;
            state = _state;
        }

        state?.Channel.Writer.TryComplete();

        return state;
    }

    private async Task _ConsumeAsync(
        ChannelReader<CacheEventDispatchEntry> reader,
        CancellationTokenSource cancellation
    )
    {
        var cancellationToken = cancellation.Token;

        try
        {
            await foreach (var entry in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await entry.InvokeAsync(HandlerErrorCallback, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    HandlerErrorCallback(exception);
                }
                finally
                {
                    Interlocked.Increment(ref _processed);
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A bounded shutdown abandons whatever remains after the drain deadline.
        }
        finally
        {
            var abandoned = 0L;

            while (reader.TryRead(out _))
            {
                abandoned++;
            }

            if (abandoned != 0)
            {
                Interlocked.Add(ref _pending, -abandoned);
                _RecordDropped(abandoned);
            }

            cancellation.Dispose();
        }
    }

    private void _RecordDropped(long count)
    {
        var dropped = Interlocked.Add(ref _dropped, count);
        CachingMetrics.RecordEventDropped(_cacheName, count);

        if (_logger?.IsEnabled(LogLevel.Warning) == true && (dropped == 1 || (dropped & (dropped - 1)) == 0))
        {
            _logger.LogWarning(
                "The bounded cache event FIFO for {CacheName} has dropped {DroppedCount} signals. "
                    + "Its capacity is {BufferCapacity}; producers are never blocked.",
                _cacheName,
                dropped,
                _capacity
            );
        }
    }

    private void _LogDrainTimeout()
    {
        if (_logger?.IsEnabled(LogLevel.Warning) == true)
        {
            _logger.LogWarning(
                "The cache event FIFO for {CacheName} did not drain within {DrainTimeout}; "
                    + "{PendingCount} signals remain and will be abandoned.",
                _cacheName,
                _shutdownDrainTimeout,
                Interlocked.Read(ref _pending)
            );
        }
    }

    private static Action<Exception> _CreateErrorLogger(ILogger? logger, LogLevel errorLevel)
    {
        if (logger is null)
        {
            return static _ => { };
        }

        return exception =>
        {
            if (logger.IsEnabled(errorLevel))
            {
                logger.Log(
                    errorLevel,
                    exception,
                    "An exception was thrown by a cache event handler and was suppressed."
                );
            }
        };
    }

    private sealed record DispatchState(
        Channel<CacheEventDispatchEntry> Channel,
        CancellationTokenSource Cancellation,
        Task Worker
    );

    private readonly struct CacheEventDispatchEntry(
        object handlerSnapshot,
        object sender,
        EventArgs args,
        ICacheEventDispatchTarget target
    )
    {
        public static CacheEventDispatchEntry Create<TArgs>(object handlerSnapshot, object sender, TArgs args)
            where TArgs : EventArgs => new(handlerSnapshot, sender, args, CacheEventDispatchTarget<TArgs>.Instance);

        public ValueTask InvokeAsync(Action<Exception> onHandlerError, CancellationToken cancellationToken) =>
            target.InvokeAsync(handlerSnapshot, sender, args, onHandlerError, cancellationToken);
    }

    private interface ICacheEventDispatchTarget
    {
        ValueTask InvokeAsync(
            object handlerSnapshot,
            object sender,
            EventArgs args,
            Action<Exception> onHandlerError,
            CancellationToken cancellationToken
        );
    }

    private sealed class CacheEventDispatchTarget<TArgs> : ICacheEventDispatchTarget
        where TArgs : EventArgs
    {
        public static readonly CacheEventDispatchTarget<TArgs> Instance = new();

        private CacheEventDispatchTarget() { }

        public ValueTask InvokeAsync(
            object handlerSnapshot,
            object sender,
            EventArgs args,
            Action<Exception> onHandlerError,
            CancellationToken cancellationToken
        ) =>
            AsyncEvent<TArgs>.SafeInvokeHandlerSnapshotAsync(
                handlerSnapshot,
                sender,
                (TArgs)args,
                onHandlerError,
                cancellationToken
            );
    }
}
