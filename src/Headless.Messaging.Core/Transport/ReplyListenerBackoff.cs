// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// The delay a reply listener waits before it tries again to open its channel: one second at first, doubling after
/// each failed attempt up to thirty seconds, and back to one second once the channel is open.
/// </summary>
internal sealed class ReplyListenerBackoff(TimeProvider timeProvider)
{
    private static readonly TimeSpan _FirstDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>The delay the next <see cref="WaitAsync"/> waits.</summary>
    public TimeSpan Delay { get; private set; } = _FirstDelay;

    /// <summary>Starts the sequence over, once the channel is open again.</summary>
    public void Reset()
    {
        Delay = _FirstDelay;
    }

    /// <summary>Waits <see cref="Delay"/>, then doubles it up to the cap.</summary>
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

        Delay = TimeSpan.FromTicks(Math.Min(Delay.Ticks * 2, _MaxDelay.Ticks));
        return true;
    }
}
