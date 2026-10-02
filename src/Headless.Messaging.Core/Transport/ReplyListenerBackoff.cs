// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// The delay a reply listener waits before it tries again to open its channel, after a failed attempt or a lost channel
/// alike: about one second at first, doubling up to thirty seconds, and back to about one second once the channel is
/// open. Each delay is jittered, so the listeners of a fleet that loses its broker at once do not reconnect in step.
/// </summary>
/// <param name="timeProvider">The clock the waits run on.</param>
/// <param name="jitter">The jitter source; <see cref="Random.Shared"/> when omitted.</param>
internal sealed class ReplyListenerBackoff(TimeProvider timeProvider, Random? jitter = null)
{
    private static readonly TimeSpan _FirstDelay = TimeSpan.FromSeconds(1);

    private readonly Random _jitter = jitter ?? Random.Shared;
    private TimeSpan _nominal = _FirstDelay;
    private TimeSpan? _delay;

    /// <summary>The jittered delay the next <see cref="WaitAsync"/> waits.</summary>
    public TimeSpan Delay => _delay ??= ReconnectBackoff.Jitter(_nominal, _FirstDelay, _jitter);

    /// <summary>Starts the sequence over, once the channel is open again.</summary>
    public void Reset()
    {
        _nominal = _FirstDelay;
        _delay = null;
    }

    /// <summary>Waits <see cref="Delay"/>, then doubles the delay up to the cap.</summary>
    /// <param name="closingToken">The listener's closing token.</param>
    /// <returns><see langword="false"/> when the listener closed during the wait; otherwise <see langword="true"/>.</returns>
    public async ValueTask<bool> WaitAsync(CancellationToken closingToken)
    {
        try
        {
            await Task.Delay(Delay, timeProvider, closingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (closingToken.IsCancellationRequested)
        {
            return false;
        }

        _nominal = TimeSpan.FromTicks(Math.Min(_nominal.Ticks * 2, ReconnectBackoff.Ceiling.Ticks));
        _delay = null;
        return true;
    }
}
