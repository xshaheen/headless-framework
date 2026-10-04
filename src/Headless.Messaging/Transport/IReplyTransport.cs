// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Provider seam for request/reply: a reply channel addressed to one process, outside the Bus and Queue lanes.
/// </summary>
/// <remarks>
/// <para>
/// A provider registers an implementation only when it declares request/reply support in its transport capabilities.
/// A requesting host opens one listener and stamps its address on each request; a responding host sends the reply to
/// the address the request carries. Replies are never written to a lane message name, never stored by the broker, and
/// never reach another process.
/// </para>
/// <para>
/// A request names its own reply address, so the address is untrusted input on the responding host. Every address
/// lives under <see cref="ReplyAddresses.Prefix"/>, and an implementation must never write anywhere else: otherwise a
/// forged request could make a responder write to an arbitrary queue or stream with its broker credentials.
/// </para>
/// <para>
/// <b>Evolution policy:</b> this is a transport-provider extension point, so it evolves additively. New members ship
/// as default interface methods with a behavior-preserving fallback.
/// </para>
/// </remarks>
[PublicAPI]
public interface IReplyTransport
{
    /// <summary>
    /// Returns whether <paramref name="address"/> is a reply address this transport can write to. The messaging core
    /// sends only to an address that passes both this check and <see cref="ReplyAddresses.IsInReplyNamespace"/>.
    /// </summary>
    /// <remarks>
    /// The default accepts exactly the reserved reply namespace. Override it to also reject addresses the broker would
    /// misread, such as wildcard tokens or names over a broker length limit, but never to accept an address outside the
    /// namespace.
    /// </remarks>
    /// <param name="address">The address a request carried.</param>
    bool IsReplyAddress(string address)
    {
        return ReplyAddresses.IsInReplyNamespace(address);
    }

    /// <summary>
    /// Opens this process's reply listener. Each listener gets a fresh address, so replies to requests sent through an
    /// earlier listener, including one from a previous run of the process, never reach it.
    /// </summary>
    /// <remarks>
    /// The listener re-establishes itself after a connection loss. Its address can change when it does, so a request
    /// sent to the old address times out instead of reaching another process.
    /// </remarks>
    /// <param name="onReply">
    /// Invoked once per reply received, one at a time. The token is canceled when the listener closes. An exception it
    /// throws is logged and does not stop the listener.
    /// </param>
    /// <param name="cancellationToken">Token to cancel opening the listener.</param>
    /// <returns>The open listener; dispose it to close the channel and remove its broker objects.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    ValueTask<IReplyListener> OpenListenerAsync(
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends <paramref name="reply"/> to the listener at <paramref name="address"/>. A reply to an address nobody listens
    /// on any more is dropped without error, as the caller has gone.
    /// </summary>
    /// <param name="address">A reply address that <see cref="IsReplyAddress"/> accepts.</param>
    /// <param name="reply">The reply message.</param>
    /// <param name="cancellationToken">Token to cancel the send.</param>
    /// <exception cref="ArgumentException"><paramref name="address"/> is not a reply address of this transport.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    ValueTask SendAsync(string address, TransportMessage reply, CancellationToken cancellationToken = default);
}
