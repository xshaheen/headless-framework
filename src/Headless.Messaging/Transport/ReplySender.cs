// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.RequestReply;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Transport;

/// <summary>
/// The single path a reply leaves a responding host by. It refuses any destination outside the reserved reply
/// namespace before the transport sees it, and it sends each reply at most once.
/// </summary>
internal sealed class ReplySender(IReplyTransport transport, ILogger<ReplySender> logger)
{
    /// <summary>Sends <paramref name="reply"/> to the reply address a request carried.</summary>
    /// <param name="address">The request's <see cref="Headers.ReplyTo"/> value, which is untrusted wire data.</param>
    /// <param name="reply">The reply message.</param>
    /// <param name="cancellationToken">Token to cancel the send.</param>
    /// <returns>
    /// Whether the transport accepted the reply, the address was refused, or the send failed. Either failure is logged,
    /// and this reply is never retried.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    public async ValueTask<ReplySendOutcome> SendAsync(
        string? address,
        TransportMessage reply,
        CancellationToken cancellationToken
    )
    {
        reply.Headers.TryGetValue(Headers.InReplyTo, out var requestId);

        // Both checks run: the core holds every provider to the shared namespace, and the provider can narrow it further.
        if (!ReplyAddresses.IsInReplyNamespace(address) || !transport.IsReplyAddress(address))
        {
            logger.ReplyAddressRefused(
                ReplyProtocol.SanitizeRequestId(requestId),
                LogSanitizer.Sanitize(address, ReplyAddresses.MaxLength)
            );
            MessagingMetrics.RecordDroppedReply(MessagingMetrics.DropReasonInvalidReplyAddress);
            return ReplySendOutcome.AddressRefused;
        }

        try
        {
            await transport.SendAsync(address, reply, cancellationToken).ConfigureAwait(false);
            return ReplySendOutcome.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.ReplySendFailed(e, ReplyProtocol.SanitizeRequestId(requestId));
            return ReplySendOutcome.Failed;
        }
    }
}

/// <summary>What became of one reply <see cref="ReplySender"/> was asked to send.</summary>
internal enum ReplySendOutcome
{
    /// <summary>The transport accepted the reply.</summary>
    Sent,

    /// <summary>The reply address is not one a reply may go to, so nothing reached the transport.</summary>
    AddressRefused,

    /// <summary>The transport refused or failed the send, for example because the reply was too large.</summary>
    Failed,
}
