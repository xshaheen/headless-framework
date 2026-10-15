// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Retry;

/// <summary>
/// Slides a message's dispatch lease forward while one publish or consume attempt runs, and cancels the attempt when
/// the lease is lost.
/// </summary>
/// <remarks>
/// <para>
/// A lease the attempt cannot renew may already belong to another node, so the attempt must stop and write nothing:
/// its fenced state write would carry an identity the row no longer has. A renewal the store does not confirm within
/// one interval counts as lost too, because the lease may lapse before a slow store answers.
/// </para>
/// <para>
/// Stopping never cancels a renewal already in flight. A renewal that committed but was cancelled before it returned
/// would leave the message holding the old lease identity, and the attempt's next fenced write would then fail against
/// its own row. Stopping waits for that renewal instead, which the interval bounds.
/// </para>
/// <para>
/// The cadence runs on the app clock, so a test advances it like any other schedule; the lease itself is stamped by
/// the store's clock.
/// </para>
/// </remarks>
internal sealed class MessageLeaseRenewal : IAsyncDisposable
{
    private readonly MediumMessage _message;
    private readonly Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> _renew;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _attempt;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private int _lost;
    private int _stopped;

    private MessageLeaseRenewal(
        MediumMessage message,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> renew,
        TimeSpan leaseDuration,
        TimeSpan interval,
        TimeProvider clock,
        ILogger logger,
        CancellationToken attemptToken
    )
    {
        _message = message;
        _renew = renew;
        _leaseDuration = leaseDuration;
        _interval = interval;
        _clock = clock;
        _logger = logger;
        _attempt = CancellationTokenSource.CreateLinkedTokenSource(attemptToken);
        _loop = _RenewLoopAsync();
    }

    /// <summary>The attempt's token: cancelled with the caller's token, and when the lease is lost.</summary>
    public CancellationToken AttemptToken => _attempt.Token;

    /// <summary>Whether a renewal found the lease gone; the attempt must then write no state.</summary>
    public bool IsLost => Volatile.Read(ref _lost) == 1;

    /// <summary>Starts renewing <paramref name="message"/>'s lease every <paramref name="interval"/>.</summary>
    public static MessageLeaseRenewal Start(
        MediumMessage message,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> renew,
        TimeSpan leaseDuration,
        TimeSpan interval,
        TimeProvider clock,
        ILogger logger,
        CancellationToken attemptToken
    )
    {
        return new MessageLeaseRenewal(message, renew, leaseDuration, interval, clock, logger, attemptToken);
    }

    /// <summary>
    /// Stops renewing and waits for a renewal in flight, so the message's lease identity is settled before the attempt
    /// writes state with it. Safe to call more than once.
    /// </summary>
    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
        }

        await _loop.ConfigureAwait(false);
    }

    /// <summary>Throws <see cref="MessageLeaseLostException"/> when the lease was lost.</summary>
    public void ThrowIfLost()
    {
        if (IsLost)
        {
            throw new MessageLeaseLostException(_message.StorageId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopping.Dispose();
        _attempt.Dispose();
    }

    private async Task _RenewLoopAsync()
    {
        // Yield first, so the constructor returns before the loop runs, and wait one interval before the first renewal:
        // an attempt that ends within one interval writes no renewal.
        await Task.Yield();

        while (true)
        {
            try
            {
                await Task.Delay(_interval, _clock, _stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool renewed;
            try
            {
                // Bounded by one interval, not by the stop signal; see the remarks.
                using var timeout = new CancellationTokenSource(_interval, _clock);
                renewed = await _renew(_message, _leaseDuration, timeout.Token).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                // The store cannot renew; the attempt keeps the lease its reservation stamped, as it always has.
                return;
            }
            catch (Exception exception)
            {
                _logger.MessageLeaseRenewalFailed(exception, _message.StorageId);
                renewed = false;
            }

            if (!renewed)
            {
                Volatile.Write(ref _lost, 1);
                _logger.MessageLeaseLost(_message.StorageId);
                await _attempt.CancelAsync().ConfigureAwait(false);
                return;
            }
        }
    }
}

/// <summary>The attempt's lease was lost while it ran, so the attempt ends without writing state.</summary>
internal sealed class MessageLeaseLostException(Guid storageId)
    : InvalidOperationException(
        $"The lease on message '{storageId}' was lost while its attempt ran; the attempt writes no state."
    );
