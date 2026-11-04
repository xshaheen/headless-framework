// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis;

#pragma warning disable MA0134 // The owned subscribe task is awaited on dispose.
/// <summary>
/// Carries lock-release wake-ups between hosts over a Redis pub/sub channel, so contended locks wake their waiters on
/// every host without a message broker.
/// </summary>
/// <remarks>
/// <para>
/// The channel is the lock key prefix followed by <c>released</c>, so hosts that share a Redis server but use different
/// key prefixes do not wake each other's waiters. Pub/sub delivers at most once and only to connected subscribers, which
/// matches the signal's contract: a waiter that misses a release still wakes on its backoff poll.
/// </para>
/// <para>
/// The subscription is made in the background when the signal is first built, retried with a jittered back-off while
/// Redis is unreachable, and then kept by the multiplexer, which re-subscribes after a reconnect. The host never waits
/// for it to start.
/// </para>
/// <para>
/// The listeners are resolved on the first delivered release, not in the constructor: they are the lock providers,
/// which depend on this signal.
/// </para>
/// </remarks>
internal sealed class RedisDistributedLockReleaseSignal : IDistributedLockReleaseSignal, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _connection;
    private readonly IServiceProvider _services;
    private readonly ILogger<RedisDistributedLockReleaseSignal> _logger;
    private readonly CancellationTokenSource _disposeTokenSource = new();
    private readonly Task _subscribeTask;
    private readonly Action<RedisChannel, RedisValue> _handler;
    private IDistributedLockReleaseListener[]? _listeners;

    public RedisDistributedLockReleaseSignal(
        IConnectionMultiplexer connection,
        IOptions<DistributedLockOptions> lockOptions,
        IServiceProvider services,
        ILogger<RedisDistributedLockReleaseSignal> logger
    )
    {
        _connection = connection;
        _services = services;
        _logger = logger;
        // Literal, so the prefix is matched exactly and never read as a pattern.
        Channel = RedisChannel.Literal(lockOptions.Value.KeyPrefix + "released");
        _handler = (_, message) => OnMessage(message);
        _subscribeTask = Task.Run(_SubscribeAsync);
    }

    internal RedisChannel Channel { get; }

    /// <summary>
    /// Publishes the release without waiting for Redis: the signal is a best-effort hint, so a fire-and-forget publish
    /// keeps the release path free of a round trip. A failure is logged and never surfaces to the releasing caller.
    /// </summary>
    public void Announce(string resource, string leaseId)
    {
        try
        {
            _connection.GetSubscriber().Publish(Channel, EncodePayload(resource, leaseId), CommandFlags.FireAndForget);
        }
#pragma warning disable CA1031 // The release already succeeded in storage; a lost wake-up only delays waiters until their poll.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogRedisLockReleasePublishFailed(exception, resource, leaseId);
        }
    }

    /// <summary>Stops the subscribe loop and removes the channel subscription. The shared multiplexer is not disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        await _disposeTokenSource.CancelAsync().ConfigureAwait(false);
        await _subscribeTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        try
        {
            // Only this handler: another subscriber on the shared multiplexer may listen on the same channel.
            await _connection.GetSubscriber().UnsubscribeAsync(Channel, _handler).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Shutdown path: the subscription dies with the connection anyway.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogRedisLockReleaseUnsubscribeFailed(exception);
        }

        _disposeTokenSource.Dispose();
    }

    /// <summary>
    /// Encodes a release as the lease id's length, a colon, the lease id, then the resource. The length prefix lets the
    /// resource contain any character, including the separator.
    /// </summary>
    internal static string EncodePayload(string resource, string leaseId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{leaseId.Length}:{leaseId}{resource}");
    }

    /// <summary>Decodes a payload written by <see cref="EncodePayload"/>; returns <see langword="false"/> for any other text.</summary>
    internal static bool TryDecodePayload(string? payload, out string resource, out string leaseId)
    {
        resource = string.Empty;
        leaseId = string.Empty;

        if (payload is null)
        {
            return false;
        }

        var separator = payload.IndexOf(':', StringComparison.Ordinal);

        if (
            separator <= 0
            || !int.TryParse(
                payload.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var length
            )
            || length > payload.Length - separator - 1
        )
        {
            return false;
        }

        leaseId = payload.Substring(separator + 1, length);
        resource = payload[(separator + 1 + length)..];

        return true;
    }

    internal void OnMessage(RedisValue message)
    {
        if (!TryDecodePayload(message, out var resource, out var leaseId))
        {
            _logger.LogRedisLockReleaseMalformed();
            return;
        }

        var listeners = _listeners ??= [.. _services.GetServices<IDistributedLockReleaseListener>()];

        foreach (var listener in listeners)
        {
            listener.OnReleased(resource, leaseId);
        }
    }

    private async Task _SubscribeAsync()
    {
        var cancellationToken = _disposeTokenSource.Token;
        // Real time, not the app clock: it waits for Redis to come back.
        var backoff = new ReconnectBackoff();
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _connection
                    .GetSubscriber()
                    .SubscribeAsync(Channel, _handler)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Retried below; until it succeeds waiters fall back to their backoff poll.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.LogRedisLockReleaseSubscribeRetrying(exception, ++attempt);

                if (!await backoff.WaitAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
    }
}
#pragma warning restore MA0134

internal static partial class RedisDistributedLockReleaseSignalLog
{
    [LoggerMessage(
        EventId = 40,
        EventName = "RedisLockReleasePublishFailed",
        Level = LogLevel.Warning,
        Message = "Lock released but the Redis wake-up publish failed; waiters will fall back to polling: R={Resource} Id={LeaseId}"
    )]
    internal static partial void LogRedisLockReleasePublishFailed(
        this ILogger logger,
        Exception exception,
        string resource,
        string leaseId
    );

    [LoggerMessage(
        EventId = 41,
        EventName = "RedisLockReleaseSubscribeRetrying",
        Level = LogLevel.Warning,
        Message = "Subscribing to Redis lock-release wake-ups failed (attempt {Attempt}); waiters poll until it succeeds"
    )]
    internal static partial void LogRedisLockReleaseSubscribeRetrying(
        this ILogger logger,
        Exception exception,
        int attempt
    );

    [LoggerMessage(
        EventId = 42,
        EventName = "RedisLockReleaseMalformed",
        Level = LogLevel.Warning,
        Message = "Ignored a Redis lock-release message that is not in the expected format"
    )]
    internal static partial void LogRedisLockReleaseMalformed(this ILogger logger);

    [LoggerMessage(
        EventId = 43,
        EventName = "RedisLockReleaseUnsubscribeFailed",
        Level = LogLevel.Debug,
        Message = "Removing the Redis lock-release subscription failed during shutdown"
    )]
    internal static partial void LogRedisLockReleaseUnsubscribeFailed(this ILogger logger, Exception exception);
}
