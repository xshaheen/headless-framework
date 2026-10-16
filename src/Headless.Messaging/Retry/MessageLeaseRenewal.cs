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
/// A renewal that finds the lease gone means it may already belong to another node, so the attempt must stop and write
/// nothing: its fenced state write would carry an identity the row no longer has.
/// </para>
/// <para>
/// A renewal that throws or times out says nothing about the lease, only that the store did not answer, so a short
/// store blip does not abort a healthy attempt. The loop tries again on the next tick while the lease it last
/// confirmed is still live, and gives up only once that lease could lapse before the next tick. That deadline counts
/// from the moment the last confirmed renewal started (or the loop started, right after the reservation stamped the
/// lease), which is no later than the store's own stamp, so the estimate errs on the early side. A later renewal is
/// still fenced on the identity the attempt holds, so tolerating a failure never revives a lease another node took.
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
    private long _lastConfirmedAt;
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
        _lastConfirmedAt = clock.GetTimestamp();
        _loop = _RenewLoopAsync();
    }

    /// <summary>The attempt's token: cancelled with the caller's token, and when the lease is lost.</summary>
    public CancellationToken AttemptToken => _attempt.Token;

    /// <summary>Whether a renewal found the lease gone; the attempt must then write no state.</summary>
    public bool IsLost => Volatile.Read(ref _lost) == 1;

    /// <summary>
    /// Starts renewing <paramref name="message"/>'s lease for <see cref="RetryPolicyOptions.DispatchTimeout"/> on the
    /// policy's renewal cadence.
    /// </summary>
    public static MessageLeaseRenewal Start(
        MediumMessage message,
        Func<MediumMessage, TimeSpan, CancellationToken, ValueTask<bool>> renew,
        RetryPolicyOptions policy,
        TimeProvider clock,
        ILogger logger,
        CancellationToken attemptToken
    )
    {
        return new MessageLeaseRenewal(
            message,
            renew,
            policy.DispatchTimeout,
            policy.ResolveLeaseRenewalInterval(),
            clock,
            logger,
            attemptToken
        );
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
        // Waits one interval before the first renewal: an attempt that ends within one interval writes no renewal.
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

            var renewalStartedAt = _clock.GetTimestamp();
            bool renewed;
            try
            {
                // Bounded by one interval, not by the stop signal; see the remarks. The bound waits real time: on an
                // app clock that a test host fakes and never advances, a hung store call would never time out.
                using var timeout = new CancellationTokenSource(_interval, TimeProvider.System);
                renewed = await _renew(_message, _leaseDuration, timeout.Token).ConfigureAwait(false);
            }
            catch (NotSupportedException)
            {
                // The store cannot renew; the attempt keeps the lease its reservation stamped.
                return;
            }
            catch (Exception exception)
            {
                // Lost only once the last confirmed lease could lapse before the next tick; until then, try again.
                if (_clock.GetElapsedTime(_lastConfirmedAt) + _interval < _leaseDuration)
                {
                    _logger.MessageLeaseRenewalFailed(exception, _message.StorageId);
                    continue;
                }

                _logger.MessageLeaseRenewalFailedPastLease(exception, _message.StorageId);
                renewed = false;
            }

            if (renewed)
            {
                _lastConfirmedAt = renewalStartedAt;
            }
            else
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
