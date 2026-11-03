// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal sealed class RedisConnectionPool : IRedisConnectionPool, IDisposable, IAsyncDisposable
{
    // Fixed slots rather than a bag: a slot whose connect attempt failed has to be replaced in place,
    // because Lazy caches the faulted task for the lifetime of the instance.
    private readonly AsyncLazyRedisConnection[] _connections;
    private readonly Lock _evictionLock = new();

    private readonly ILoggerFactory _loggerFactory;
    private readonly RedisMessagingOptions _redisOptions;
    private int _isDisposed;
    private int _nextIndex = -1;

    public RedisConnectionPool(IOptions<RedisMessagingOptions> options, ILoggerFactory loggerFactory)
    {
        _redisOptions = options.Value;
        _loggerFactory = loggerFactory;
        // A shared multiplexer belongs to the app, so the pool opens and disposes no connection of its own.
        _connections = new AsyncLazyRedisConnection[
            _redisOptions.ConnectionMultiplexer is null ? _redisOptions.ConnectionPoolSize : 0
        ];

        for (var index = 0; index < _connections.Length; index++)
        {
            _connections[index] = _CreateConnection();
        }
    }

    // How many pooled connections have started connecting. Connections open lazily, on first use, so a test can show
    // that constructing the pool attempts none without timing the constructor.
    internal int CreatedConnectionCount => _connections.Count(static c => c.IsValueCreated);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _DisposeCreatedConnections();

        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await _DisposeCreatedConnectionsAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    public async Task<IConnectionMultiplexer> ConnectAsync(CancellationToken cancellationToken = default)
    {
        Ensure.NotDisposed(Volatile.Read(ref _isDisposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_redisOptions.ConnectionMultiplexer is { } shared)
        {
            return shared;
        }

        // Taken in turn rather than by load: every publish and poll lands here, and reading each multiplexer's
        // outstanding-command counters allocates and takes its queue locks, while no command the transport sends
        // blocks a multiplexer long enough for load-aware picking to pay for that.
        var index =
            _connections.Length == 1
                ? 0
                : (int)((uint)Interlocked.Increment(ref _nextIndex) % (uint)_connections.Length);
        var lazy = Volatile.Read(ref _connections[index]);

        if (lazy.CreatedConnection is { } createdConnection)
        {
            return createdConnection.Connection;
        }

        return (await _ResolveAsync(index, lazy, cancellationToken).ConfigureAwait(false)).Connection;
    }

    private async Task<RedisConnection> _ResolveAsync(
        int index,
        AsyncLazyRedisConnection lazy,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await lazy.GetValueAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            _EvictFaulted(index, lazy);

            throw;
        }
    }

    /// <summary>
    /// Replaces a slot whose connect attempt failed. <see cref="Lazy{T}"/> caches the faulted task for the
    /// lifetime of the instance, so without this a single unreachable-Redis window at startup makes every
    /// later caller rethrow that same failure until the process restarts.
    /// </summary>
    private void _EvictFaulted(int index, AsyncLazyRedisConnection faulted)
    {
        if (Volatile.Read(ref _isDisposed) != 0 || !faulted.IsValueCreated)
        {
            return;
        }

        var connectTask = faulted.Value;

        // A cancelled wait is the caller's own token, not a broken connection: the attempt may still be
        // in flight and shared with other callers, so only a completed failure evicts.
        if (!connectTask.IsCompleted || connectTask.IsCompletedSuccessfully)
        {
            return;
        }

        // Whoever observes the failure first installs the replacement; the rest share it rather than each
        // opening a connection of their own. Only the failure path takes this lock.
        lock (_evictionLock)
        {
            if (!ReferenceEquals(_connections[index], faulted))
            {
                return;
            }

            Volatile.Write(ref _connections[index], _CreateConnection());
        }
    }

    private AsyncLazyRedisConnection _CreateConnection()
    {
        return new AsyncLazyRedisConnection(_redisOptions, _loggerFactory.CreateLogger<AsyncLazyRedisConnection>());
    }

    private void _DisposeCreatedConnections()
    {
        foreach (var connection in _connections)
        {
            if (!connection.IsValueCreated)
            {
                continue;
            }

            connection.CreatedConnection?.Dispose();
        }
    }

    private async ValueTask _DisposeCreatedConnectionsAsync()
    {
        foreach (var connection in _connections)
        {
            if (!connection.IsValueCreated)
            {
                continue;
            }

            RedisConnection createdConnection;

            try
            {
                createdConnection = await connection.GetValueAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            catch (RedisException)
            {
                continue;
            }

            createdConnection.Dispose();
        }
    }

#pragma warning disable MA0055 // Dispose methods should call SuppressFinalize
    ~RedisConnectionPool()
#pragma warning restore MA0055
    {
        if (Volatile.Read(ref _isDisposed) == 0)
        {
            System.Diagnostics.Debug.Fail(
                "RedisConnectionPool was not disposed. Call Dispose() or DisposeAsync() to release resources."
            );
        }
    }
}
