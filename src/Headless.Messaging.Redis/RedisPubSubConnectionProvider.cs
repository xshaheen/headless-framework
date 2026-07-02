// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

internal interface IRedisPubSubConnectionProvider : IAsyncDisposable
{
    Task<IConnectionMultiplexer> ConnectAsync();
}

internal sealed class RedisPubSubConnectionProvider(IOptions<RedisPubSubOptions> optionsAccessor)
    : IRedisPubSubConnectionProvider
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ConnectionMultiplexer? _connection;

    public async Task<IConnectionMultiplexer> ConnectAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _lock.WaitAsync().ConfigureAwait(false);

        try
        {
#pragma warning disable CA1508 // Justification: other threads can initialize it while waiting for the lock.
            _connection ??= await ConnectionMultiplexer
                .ConnectAsync(optionsAccessor.Value.Configuration!)
                .ConfigureAwait(false);
#pragma warning restore CA1508
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
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
