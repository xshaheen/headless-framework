// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Headless.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Caching;

#pragma warning disable MA0134 // The owned establish loop is awaited on dispose.
/// <summary>
/// Carries hybrid-cache invalidations between hosts over a Redis pub/sub channel, so the hosts that share a Redis server
/// evict each other's stale L1 entries without a message broker.
/// </summary>
/// <remarks>
/// <para>
/// The channel is the default Redis cache's key prefix followed by <see cref="CacheInvalidationMessage.MessageName"/>, so
/// applications that share a Redis server under different key prefixes do not flush each other's L1. Each message is the
/// <see cref="CacheInvalidationMessage"/> as JSON.
/// </para>
/// <para>
/// Pub/sub delivers at most once and only to connected subscribers, so every establishment of the subscription is
/// followed by an L1 flush on every listener: the first one, because L1 can be warmed before the subscription is live,
/// and each one after the multiplexer restores a connection, because invalidations sent while it was down never
/// arrived. After a restore the subscription is dropped and made again, and the flush waits for Redis to confirm it, so
/// no invalidation can slip between the flush and a live subscription.
/// </para>
/// <para>
/// The subscription is made in the background when the backplane is first built, retried with a jittered back-off while
/// Redis is unreachable; the host never waits for it. The listeners are resolved on the first delivery, not in the
/// constructor: they reach the hybrid caches, which depend on this backplane.
/// </para>
/// </remarks>
internal sealed class RedisHybridCacheBackplane : IHybridCacheBackplane, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _connection;
    private readonly IServiceProvider _services;
    private readonly ILogger<RedisHybridCacheBackplane> _logger;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly CancellationToken _stopping;

    // Capacity one with DropWrite coalesces a burst of restores (one per endpoint and connection type) into a single
    // re-establishment queued behind the one in progress.
    private readonly Channel<bool> _reestablishRequests = System.Threading.Channels.Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true }
    );

    private readonly Task _establishLoop;
    private ChannelMessageQueue? _subscription;
    private IHybridCacheInvalidationListener[]? _listeners;
    private long _generation;
    private int _isDisposed;

    public RedisHybridCacheBackplane(
        RedisCacheOptions? redisOptions,
        IServiceProvider services,
        ILogger<RedisHybridCacheBackplane> logger
    )
    {
        _connection =
            redisOptions?.ConnectionMultiplexer
            ?? services.GetService<IConnectionMultiplexer>()
            ?? throw new InvalidOperationException(
                "UseRedisBackplane() needs a Redis connection: register an IConnectionMultiplexer, or set "
                    + "RedisCacheOptions.ConnectionMultiplexer in UseRedis(...) or AddRedisTier(...)."
            );

        _services = services;
        _logger = logger;
        _stopping = _disposeTokenSource.Token;

        // Literal, so the prefix is matched exactly and never read as a pattern.
        Channel = RedisChannel.Literal((redisOptions?.KeyPrefix ?? "") + CacheInvalidationMessage.MessageName);

        _connection.ConnectionRestored += _OnConnectionRestored;
        _establishLoop = Task.Run(_RunEstablishLoopAsync);
    }

    internal RedisChannel Channel { get; }

    /// <summary>How many establishments of the subscription have completed, flush included.</summary>
    internal long EstablishedGeneration => Volatile.Read(ref _generation);

    /// <summary>
    /// Publishes the invalidation and waits for Redis to accept it, so a failure reaches the cache, which logs it and
    /// queues it for replay under auto-recovery.
    /// </summary>
    public async ValueTask PublishAsync(CacheInvalidationMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            RedisHybridCacheBackplaneJsonContext.Default.CacheInvalidationMessage
        );

        // StackExchange.Redis takes no token, so the wait honours the caller's cancellation and the command runs on.
        await _connection
            .GetSubscriber()
            .PublishAsync(Channel, payload)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Stops the establish loop and removes the subscription. The shared multiplexer is not disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _connection.ConnectionRestored -= _OnConnectionRestored;
        await _disposeTokenSource.CancelAsync().ConfigureAwait(false);
        _reestablishRequests.Writer.TryComplete();
        await _establishLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (_subscription is { } subscription)
        {
            try
            {
                // Only this queue: another subscriber on the shared multiplexer may listen on the same channel.
                await subscription.UnsubscribeAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Shutdown path: the subscription dies with the connection anyway.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogRedisBackplaneUnsubscribeFailed(exception);
            }
        }

        _disposeTokenSource.Dispose();
    }

    internal async Task OnMessageAsync(RedisValue payload)
    {
        CacheInvalidationMessage? message;

        try
        {
            message = JsonSerializer.Deserialize(
                (byte[]?)payload,
                RedisHybridCacheBackplaneJsonContext.Default.CacheInvalidationMessage
            );
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            _logger.LogRedisBackplaneMalformedMessage();
            return;
        }

        foreach (var listener in _GetListeners())
        {
            try
            {
                await listener.OnInvalidationAsync(message, _stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // One failing listener must not stop the others or the delivery loop; it is logged.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogRedisBackplaneListenerFailed(exception);
            }
        }
    }

    private IHybridCacheInvalidationListener[] _GetListeners()
    {
        return _listeners ??= [.. _services.GetServices<IHybridCacheInvalidationListener>()];
    }

    private void _OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        // Every connection type, not only the subscription one: on RESP3 the subscription shares the interactive
        // connection, and a spare flush after an interactive-only blip costs one L1 refill, never stale data.
        _logger.LogRedisBackplaneConnectionRestored(e.ConnectionType);
        _reestablishRequests.Writer.TryWrite(item: true);
    }

    private async Task _RunEstablishLoopAsync()
    {
        try
        {
            await _EstablishAsync().ConfigureAwait(false);

            while (await _reestablishRequests.Reader.WaitToReadAsync(_stopping).ConfigureAwait(false))
            {
                _reestablishRequests.Reader.TryRead(out _);
                await _EstablishAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Disposed.
        }
    }

    private async Task _EstablishAsync()
    {
        // Real time, not the app clock: it waits for Redis to come back.
        var backoff = new ReconnectBackoff();
        var attempt = 0;

        while (true)
        {
            try
            {
                if (_subscription is { } previous)
                {
                    // Dropped and made again rather than trusted to the multiplexer's own re-subscribe: the flush
                    // below must wait for a subscription Redis confirmed, or an invalidation sent between the flush
                    // and the re-subscribe would be lost with its L1 entry still cached.
                    await previous.UnsubscribeAsync().WaitAsync(_stopping).ConfigureAwait(false);
                    _subscription = null;
                }

                var subscription = await _connection
                    .GetSubscriber()
                    .SubscribeAsync(Channel)
                    .WaitAsync(_stopping)
                    .ConfigureAwait(false);

                // Sequential, so one host applies invalidations in the order Redis delivered them.
                subscription.OnMessage(message => OnMessageAsync(message.Message));
                _subscription = subscription;

                break;
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // Retried below; until it succeeds this host misses invalidations, and the flush after covers them.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogRedisBackplaneSubscribeRetrying(exception, ++attempt);

                if (!await backoff.WaitAsync(_stopping).ConfigureAwait(false))
                {
                    _stopping.ThrowIfCancellationRequested();
                }
            }
        }

        var generation = _generation + 1;

        foreach (var listener in _GetListeners())
        {
            try
            {
                await listener.OnSubscriptionEstablishedAsync(generation, _stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // A failed flush must not stop later establishments; it is logged.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogRedisBackplaneFlushFailed(exception, generation);
            }
        }

        Volatile.Write(ref _generation, generation);
    }
}
#pragma warning restore MA0134

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
)]
[JsonSerializable(typeof(CacheInvalidationMessage))]
internal sealed partial class RedisHybridCacheBackplaneJsonContext : JsonSerializerContext;

internal static partial class RedisHybridCacheBackplaneLog
{
    [LoggerMessage(
        EventId = 20,
        EventName = "RedisBackplaneSubscribeRetrying",
        Level = LogLevel.Warning,
        Message = "Subscribing to Redis hybrid-cache invalidations failed (attempt {Attempt}); this host misses peer invalidations until it succeeds, then flushes its L1"
    )]
    internal static partial void LogRedisBackplaneSubscribeRetrying(
        this ILogger logger,
        Exception exception,
        int attempt
    );

    [LoggerMessage(
        EventId = 21,
        EventName = "RedisBackplaneMalformedMessage",
        Level = LogLevel.Warning,
        Message = "Ignored a Redis hybrid-cache invalidation that is not a CacheInvalidationMessage"
    )]
    internal static partial void LogRedisBackplaneMalformedMessage(this ILogger logger);

    [LoggerMessage(
        EventId = 22,
        EventName = "RedisBackplaneListenerFailed",
        Level = LogLevel.Error,
        Message = "A hybrid-cache invalidation listener failed on a Redis delivery"
    )]
    internal static partial void LogRedisBackplaneListenerFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 23,
        EventName = "RedisBackplaneFlushFailed",
        Level = LogLevel.Error,
        Message = "Flushing the hybrid-cache L1 after the Redis subscription was established (generation {Generation}) failed"
    )]
    internal static partial void LogRedisBackplaneFlushFailed(
        this ILogger logger,
        Exception exception,
        long generation
    );

    [LoggerMessage(
        EventId = 24,
        EventName = "RedisBackplaneConnectionRestored",
        Level = LogLevel.Information,
        Message = "Redis {ConnectionType} connection restored; re-subscribing to hybrid-cache invalidations and flushing L1"
    )]
    internal static partial void LogRedisBackplaneConnectionRestored(
        this ILogger logger,
        ConnectionType connectionType
    );

    [LoggerMessage(
        EventId = 25,
        EventName = "RedisBackplaneUnsubscribeFailed",
        Level = LogLevel.Debug,
        Message = "Removing the Redis hybrid-cache invalidation subscription failed during shutdown"
    )]
    internal static partial void LogRedisBackplaneUnsubscribeFailed(this ILogger logger, Exception exception);
}
