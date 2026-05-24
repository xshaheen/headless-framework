// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

public class AsyncLazyRedisConnection(
    MessagingRedisOptions redisOptions,
    ILogger<AsyncLazyRedisConnection> logger,
    TimeProvider? timeProvider = null
) : Lazy<Task<RedisConnection>>(() => _ConnectAsync(redisOptions, logger, timeProvider ?? TimeProvider.System))
{
    public RedisConnection? CreatedConnection => IsValueCreated ? Value.GetAwaiter().GetResult() : null;

    public TaskAwaiter<RedisConnection> GetAwaiter()
    {
        return Value.GetAwaiter();
    }

    private static async Task<RedisConnection> _ConnectAsync(
        MessagingRedisOptions redisOptions,
        ILogger<AsyncLazyRedisConnection> logger,
        TimeProvider timeProvider
    )
    {
        var attempt = 1;

        await using var redisLogger = new RedisLogger(logger);

        ConnectionMultiplexer? connection = null;

        while (attempt <= 5)
        {
            connection = await ConnectionMultiplexer
                .ConnectAsync(redisOptions.Configuration!, redisLogger)
                .ConfigureAwait(false);

            connection.LogEvents(logger);

            if (!connection.IsConnected)
            {
                logger.LogRedisConnectionAttemptFailed(attempt);

                await timeProvider.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                ++attempt;
            }
            else
            {
                attempt = 6;
            }
        }

        if (connection == null)
        {
            throw new InvalidOperationException($"Can't establish redis connection,after [{attempt}] attempts.");
        }

        return new RedisConnection(connection);
    }
}

public sealed class RedisConnection(IConnectionMultiplexer connection) : IDisposable
{
    private bool _isDisposed;

    public IConnectionMultiplexer Connection { get; } = Argument.IsNotNull(connection);

    public long ConnectionCapacity => Connection.GetCounters().TotalOutstanding;

    public void Dispose()
    {
        _Dispose(disposing: true);
    }

    private void _Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            Connection.Dispose();
        }

        _isDisposed = true;
    }
}

internal static partial class AsyncLazyRedisConnectionLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "RedisConnectionAttemptFailed",
        Level = LogLevel.Warning,
        Message = "Can't establish redis connection,trying to establish connection [attempt {Attempt}]."
    )]
    public static partial void LogRedisConnectionAttemptFailed(this ILogger logger, int attempt);
}
