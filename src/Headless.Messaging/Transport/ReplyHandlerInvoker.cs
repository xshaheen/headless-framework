// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>Hands one received reply to a listener's handler without letting a faulty reply stop the listener.</summary>
internal static class ReplyHandlerInvoker
{
    /// <summary>Invokes <paramref name="onReply"/> for <paramref name="reply"/>.</summary>
    /// <param name="onReply">The handler the listener was opened with.</param>
    /// <param name="reply">The received reply.</param>
    /// <param name="state">The state <paramref name="onFailure"/> reports with.</param>
    /// <param name="onFailure">Reports a handler failure; the listener keeps receiving after it.</param>
    /// <param name="closingToken">The listener's closing token, passed to the handler.</param>
    /// <returns>
    /// <see langword="false"/> when the handler stopped because the listener is closing, whose pending calls the
    /// requester's shutdown fails; otherwise <see langword="true"/>.
    /// </returns>
    public static async ValueTask<bool> InvokeAsync<TState>(
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        TransportMessage reply,
        TState state,
        Action<TState, Exception> onFailure,
        CancellationToken closingToken
    )
    {
        try
        {
            await onReply(reply, closingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (closingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e)
        {
            // One faulty reply must not stop the channel every other pending call depends on.
            onFailure(state, e);
        }

        return true;
    }
}
