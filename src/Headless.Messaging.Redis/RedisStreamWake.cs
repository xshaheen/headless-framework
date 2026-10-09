// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Ends a poll loop's idle wait as soon as a publisher announces a new entry on one of the loop's streams, so an entry
/// added to an idle stream is read without waiting out the poll interval.
/// </summary>
/// <remarks>
/// StackExchange.Redis never sends a blocking <c>XREAD</c>/<c>XREADGROUP</c>, which would stall its shared
/// multiplexer, so the loops poll. Each publish follows its <c>XADD</c> with a <c>PUBLISH</c> on the stream's wake
/// channel, and this subscription turns it into an early read. Pub/sub delivers at most once and keeps nothing while a
/// subscription is down, so the poll stays the fallback: a lost wake-up only delays an entry to the next poll.
/// </remarks>
internal sealed class RedisStreamWake : IAsyncDisposable
{
    // Under steady traffic every publish would wake every loop on the stream; this caps woken reads at 20 a second per
    // loop. An entry on an idle stream, whose loop last read long ago, is still read at once.
    internal static readonly TimeSpan MinWokenReadInterval = TimeSpan.FromMilliseconds(50);

    private readonly RedisChannel[] _channels;
    private readonly IRedisConnectionPool _connectionPool;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly AsyncAutoResetEvent _signal = new();
    private readonly Action<RedisChannel, RedisValue> _onWake;
    private ISubscriber? _subscriber;
    private bool _failureLogged;

    /// <summary>Creates a wake-up for a poll loop over <paramref name="streams"/>.</summary>
    /// <param name="streams">The streams to wake on; empty for a loop that only polls.</param>
    public RedisStreamWake(
        IEnumerable<string> streams,
        IRedisConnectionPool connectionPool,
        TimeProvider timeProvider,
        ILogger logger
    )
    {
        _channels = [.. streams.Select(stream => RedisChannel.Literal(RedisPhysicalAddress.WakeChannel(stream)))];
        _connectionPool = connectionPool;
        _timeProvider = timeProvider;
        _logger = logger;
        _onWake = (_, _) => _signal.Set();
    }

    /// <summary>
    /// Subscribes to the wake channels unless already subscribed. Call before each read: an entry added after the
    /// subscription and before the read is read by it, and one added after the read wakes the next wait.
    /// </summary>
    public async Task EnsureSubscribedAsync(CancellationToken cancellationToken)
    {
        if (_subscriber is not null || _channels.Length == 0)
        {
            return;
        }

        ISubscriber? subscriber = null;

        try
        {
            var connection = await _connectionPool.ConnectAsync(cancellationToken).ConfigureAwait(false);
            subscriber = connection.GetSubscriber();

            foreach (var channel in _channels)
            {
                await subscriber.SubscribeAsync(channel, _onWake).WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            _subscriber = subscriber;
            _failureLogged = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown: the loop's next wait observes the token and ends.
        }
        catch (Exception ex)
        {
            // The loop keeps polling and tries again before its next read. One warning per failure streak keeps a
            // server that refuses pub/sub, such as an ACL without channel access, from logging on every poll.
            if (!_failureLogged)
            {
                _failureLogged = true;
                _logger.LogWakeSubscriptionFailed(ex);
            }

            if (subscriber is not null)
            {
                await _UnsubscribeAsync(subscriber).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Waits <paramref name="pollDelay"/>, or less when a publisher announces an entry first. A woken wait still lasts
    /// until <see cref="MinWokenReadInterval"/>, capped at <paramref name="pollDelay"/>, has passed since
    /// <paramref name="readStartedAt"/>.
    /// </summary>
    /// <param name="pollDelay">The longest wait: the core's poll interval.</param>
    /// <param name="readStartedAt">The <see cref="TimeProvider.GetTimestamp"/> taken when the last read started.</param>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task WaitAsync(TimeSpan pollDelay, long readStartedAt, CancellationToken cancellationToken)
    {
        if (_subscriber is null)
        {
            await _timeProvider.Delay(pollDelay, cancellationToken).ConfigureAwait(false);
            return;
        }

        using (var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var woken = _signal.WaitAsync(waitCts.Token);
            var slept = _timeProvider.Delay(pollDelay, waitCts.Token);
            var first = await Task.WhenAny(woken, slept).ConfigureAwait(false);

            // Cancelling the loser removes its waiter, so a later wake-up is kept for the next wait.
            await waitCts.CancelAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (first != woken || !woken.IsCompletedSuccessfully)
            {
                return;
            }
        }

        var interval = pollDelay < MinWokenReadInterval ? pollDelay : MinWokenReadInterval;
        var remaining = interval - _timeProvider.GetElapsedTime(readStartedAt);

        if (remaining > TimeSpan.Zero)
        {
            await _timeProvider.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _subscriber, value: null) is { } subscriber)
        {
            await _UnsubscribeAsync(subscriber).ConfigureAwait(false);
        }
    }

    // Removes only this loop's handler: other loops of the process may wake on the same channel.
    private async Task _UnsubscribeAsync(ISubscriber subscriber)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await subscriber.UnsubscribeAsync(channel, _onWake).ConfigureAwait(false);
            }
#pragma warning disable ERP022 // Best-effort cleanup: the pool may already be disposed at shutdown, and a handler left on a dropped connection is gone with it.
            catch
            {
                // ignored
            }
#pragma warning restore ERP022
        }
    }
}

internal static partial class RedisStreamWakeLog
{
    [LoggerMessage(
        EventId = 6,
        EventName = "WakeSubscriptionFailed",
        Level = LogLevel.Warning,
        Message = "Redis error when subscribing to stream wake-ups; consumers poll at the core's interval until it succeeds"
    )]
    public static partial void LogWakeSubscriptionFailed(this ILogger logger, Exception exception);
}
