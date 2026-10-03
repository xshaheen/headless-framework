// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// An open reply channel of one process, created by <see cref="IReplyTransport.OpenListenerAsync"/>. Disposing it
/// stops delivery and removes the channel's broker objects.
/// </summary>
[PublicAPI]
public interface IReplyListener : IAsyncDisposable
{
    /// <summary>
    /// Waits until the listener can receive replies and returns the address a request must carry. While the listener
    /// is re-establishing itself after a connection loss, this waits for the new address.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    /// <returns>The current reply address, inside <see cref="ReplyAddresses.Prefix"/>.</returns>
    /// <exception cref="ObjectDisposedException">The listener is closed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken = default);
}
