// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// A lazily-initialised, retrying Redis connection that establishes the <c>IConnectionMultiplexer</c>
/// on first await. Up to five connection attempts are made with a two-second delay between retries.
/// </summary>
internal sealed class AsyncLazyRedisConnection(
    RedisMessagingOptions redisOptions,
    ILogger<AsyncLazyRedisConnection> logger,
    TimeProvider? timeProvider = null,
    CancellationToken cancellationToken = default
)
    : Lazy<Task<RedisConnection>>(() =>
        _ConnectAsync(redisOptions, logger, timeProvider ?? TimeProvider.System, cancellationToken)
    )
{
    /// <summary>
    /// Returns the established <see cref="RedisConnection"/> when the lazy value has already been
    /// resolved; otherwise <see langword="null"/>.
    /// </summary>
#pragma warning disable VSTHRD104 // Offer async methods
#pragma warning disable MA0045 // The task has completed successfully, so reading Result cannot block.
    public RedisConnection? CreatedConnection => IsValueCreated && Value.IsCompletedSuccessfully ? Value.Result : null;
#pragma warning restore MA0045, VSTHRD104

    /// <summary>Returns the connection task, cancelling only this caller's wait when requested.</summary>
    public async Task<RedisConnection> GetValueAsync(CancellationToken cancellationToken = default) =>
        await Value.WaitAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Returns an awaiter so the connection can be awaited directly.</summary>
    public TaskAwaiter<RedisConnection> GetAwaiter()
    {
        return Value.GetAwaiter();
    }

    private static async Task<RedisConnection> _ConnectAsync(
        RedisMessagingOptions redisOptions,
        ILogger<AsyncLazyRedisConnection> logger,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
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

                await timeProvider.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                ++attempt;
            }
            else
            {
                attempt = 6;
            }
        }

        if (connection == null)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Can't establish redis connection,after [{attempt}] attempts."
                )
            );
        }

        return new RedisConnection(connection);
    }
}

/// <summary>
/// Wraps an established <c>IConnectionMultiplexer</c> with a capacity counter and owns its lifetime.
/// </summary>
internal sealed class RedisConnection(IConnectionMultiplexer connection) : IDisposable
{
    private bool _isDisposed;

    /// <summary>The underlying StackExchange.Redis connection multiplexer.</summary>
    public IConnectionMultiplexer Connection { get; } = Argument.IsNotNull(connection);

    /// <summary>
    /// The number of outstanding (in-flight) commands on this connection, used by the pool
    /// to select the least-loaded connection.
    /// </summary>
    public long ConnectionCapacity => Connection.GetCounters().TotalOutstanding;

    /// <inheritdoc/>
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
