// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    private bool _poolAlreadyConfigured;

    public RedisConnectionPool(IOptions<RedisMessagingOptions> options, ILoggerFactory loggerFactory)
    {
        _redisOptions = options.Value;
        _loggerFactory = loggerFactory;
        _connections = new AsyncLazyRedisConnection[_redisOptions.ConnectionPoolSize];

        for (var index = 0; index < _connections.Length; index++)
        {
            _connections[index] = _CreateConnection();
        }
    }

    private AsyncLazyRedisConnection? QuietConnection =>
        _poolAlreadyConfigured
            ? _connections.OrderBy(static c => c.CreatedConnection?.ConnectionCapacity ?? int.MaxValue).FirstOrDefault()
            : null;

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (QuietConnection is not { } quietConnection)
        {
            _poolAlreadyConfigured =
                _connections.Where(static c => c.IsValueCreated).Take(_redisOptions.ConnectionPoolSize + 1).Count()
                == _redisOptions.ConnectionPoolSize;
            quietConnection = QuietConnection;
            if (quietConnection?.CreatedConnection is { } createdConnection)
            {
                return createdConnection.Connection;
            }
        }
        else if (quietConnection.CreatedConnection is { } createdConnection)
        {
            return createdConnection.Connection;
        }

        for (var index = 0; index < _connections.Length; index++)
        {
            var lazy = Volatile.Read(ref _connections[index]);

            if (!lazy.IsValueCreated || lazy.CreatedConnection is not { } createdConnection)
            {
                return (await _ResolveAsync(index, lazy, cancellationToken).ConfigureAwait(false)).Connection;
            }

            if (createdConnection.ConnectionCapacity == 0)
            {
                return createdConnection.Connection;
            }
        }

        var selectedIndex = _SelectLeastLoadedIndex();

        return (
            await _ResolveAsync(selectedIndex, Volatile.Read(ref _connections[selectedIndex]), cancellationToken)
                .ConfigureAwait(false)
        ).Connection;
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

    private int _SelectLeastLoadedIndex()
    {
        var selectedIndex = 0;
        var lowestCapacity = long.MaxValue;

        for (var index = 0; index < _connections.Length; index++)
        {
            var capacity =
                Volatile.Read(ref _connections[index]).CreatedConnection?.ConnectionCapacity ?? long.MaxValue;

            if (capacity < lowestCapacity)
            {
                lowestCapacity = capacity;
                selectedIndex = index;
            }
        }

        return selectedIndex;
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
