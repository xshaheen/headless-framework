// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal interface IRedisPubSubConnectionProvider : IAsyncDisposable
{
    Task<IConnectionMultiplexer> ConnectAsync(CancellationToken cancellationToken = default);
}

internal sealed class RedisPubSubConnectionProvider(IOptions<RedisPubSubOptions> optionsAccessor)
    : IRedisPubSubConnectionProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ConnectionMultiplexer? _connection;
    private Task<ConnectionMultiplexer>? _connectionTask;

    public async Task<IConnectionMultiplexer> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        Task<ConnectionMultiplexer> connectionTask;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
#pragma warning disable CA1508 // Justification: other threads can initialize it while waiting for the lock.
            if (_connection is not null)
            {
                return _connection;
            }

            if (_connectionTask is null || _connectionTask.IsFaulted || _connectionTask.IsCanceled)
            {
                _connectionTask = ConnectionMultiplexer.ConnectAsync(optionsAccessor.Value.Configuration!);
            }

            connectionTask = _connectionTask;
#pragma warning restore CA1508
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            _connection = await connectionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (connectionTask.IsFaulted || connectionTask.IsCanceled)
        {
            await _lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);

            try
            {
                if (ReferenceEquals(_connectionTask, connectionTask))
                {
                    _connectionTask = null;
                }
            }
            finally
            {
                _lock.Release();
            }

            throw;
        }

        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
