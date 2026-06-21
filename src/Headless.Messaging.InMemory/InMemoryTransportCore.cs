// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.InMemory;

internal static class InMemoryTransportCore
{
    /// <summary>
    /// Shared send implementation: invokes <paramref name="send"/>, wraps any exception as a failed
    /// <see cref="OperateResult"/>, and re-throws <see cref="OperationCanceledException"/> unchanged.
    /// </summary>
    public static Task<OperateResult> SendCoreAsync(
        TransportMessage message,
        Action<TransportMessage> send,
        CancellationToken cancellationToken
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            send(message);
            return Task.FromResult(OperateResult.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            var wrapperEx = new PublisherSentFailedException(e.Message, e);
            return Task.FromResult(OperateResult.Failed(wrapperEx));
        }
    }
}
