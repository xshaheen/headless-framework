// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Exceptions;
using Headless.Messaging.Internal;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Matches each reply the listener receives to the call waiting for it and completes that call at most once.
/// </summary>
/// <remarks>
/// It never throws and never waits on a caller: calls complete through continuations that run asynchronously, so a slow
/// caller cannot stall the listener and the replies queued behind it.
/// </remarks>
internal sealed class ReplyDispatcher(PendingRequests pending, ISerializer serializer, ILogger<ReplyDispatcher> logger)
{
    public async ValueTask DispatchAsync(TransportMessage reply, CancellationToken cancellationToken)
    {
        reply.Headers.TryGetValue(Headers.InReplyTo, out var requestId);

        // A reply naming no call, or a call this process never made or has long forgotten, answers nothing here. A call
        // not yet prepared has not left the process, so a reply naming it cannot be genuine.
        if (string.IsNullOrWhiteSpace(requestId) || !pending.TryGet(requestId, out var entry) || !entry.IsPrepared)
        {
            _Drop(requestId, MessagingMetrics.DropReasonUnknown);
            return;
        }

        // An entry drops its call only after the call ended, so a missing call is a finished one.
        if (entry.Call is not { State: PendingRequestState.Pending } request)
        {
            _DropFinished(entry);
            return;
        }

        reply.Headers.TryGetValue(Headers.TenantId, out var replyTenantId);

        // Null equals only null: a reply without a tenant never answers a tenant's call, and the reverse.
        if (!string.Equals(_Normalize(replyTenantId), request.TenantId, StringComparison.Ordinal))
        {
            logger.ReplyTenantMismatch(ReplyProtocol.SanitizeRequestId(requestId));
            MessagingMetrics.RecordDroppedReply(MessagingMetrics.DropReasonTenantMismatch);
            return;
        }

        if (!request.TryClaimForReply())
        {
            _DropFinished(entry);
            return;
        }

        try
        {
            var response = await _ReadResponseAsync(request, reply, cancellationToken).ConfigureAwait(false);
            request.CompleteClaimed(response);
        }
        catch (RequestReplyException e)
        {
            request.FailClaimed(e);
        }
        catch (Exception e)
        {
            // The reply matched the expected contract but its body could not be read as the response type.
            request.FailClaimed(
                new MessageDeserializationException(
                    $"The reply to request '{request.RequestId}' could not be read as {request.ResponseType.Name}.",
                    e
                )
            );
        }
    }

    private async ValueTask<object> _ReadResponseAsync(
        PendingRequest request,
        TransportMessage reply,
        CancellationToken cancellationToken
    )
    {
        reply.Headers.TryGetValue(Headers.ReplyStatus, out var status);

        if (string.Equals(status, ReplyProtocol.StatusFault, StringComparison.Ordinal))
        {
            // An unreadable fault body still means the responder failed; the generic code says so without guessing.
            var fault = ReplyProtocol.ReadFault(reply.Body) ?? new ReplyFault(RequestFaultCodes.HandlerFailed);
            throw new RequestFaultedException(request.RequestId, fault.Code, fault.ExceptionType, fault.Detail);
        }

        reply.Headers.TryGetValue(Headers.MessageName, out var actualName);
        reply.Headers.TryGetValue(Headers.ContractVersion, out var actualVersion);

        // A reply with no recognized status follows a protocol this caller does not speak, which is a contract
        // disagreement between the two hosts like a differing response contract.
        if (
            !string.Equals(status, ReplyProtocol.StatusOk, StringComparison.Ordinal)
            || !string.Equals(actualName, request.ExpectedMessageName, StringComparison.Ordinal)
            || !string.Equals(actualVersion, request.ExpectedContractVersion, StringComparison.Ordinal)
        )
        {
            throw new ResponseContractMismatchException(
                request.RequestId,
                request.ExpectedMessageName,
                request.ExpectedContractVersion,
                actualName,
                actualVersion
            );
        }

        var message = await serializer
            .DeserializeAsync(reply, request.ResponseType, cancellationToken)
            .ConfigureAwait(false);

        // A responder that returns null sends a null_response fault instead, so an empty ok reply breaks the protocol;
        // it still surfaces as that fault rather than as a null response.
        return message.Value ?? throw new RequestFaultedException(request.RequestId, RequestFaultCodes.NullResponse);
    }

    private void _DropFinished(PendingRequestEntry entry)
    {
        _Drop(
            entry.RequestId,
            entry.State is PendingRequestState.Replied
                ? MessagingMetrics.DropReasonDuplicate
                : MessagingMetrics.DropReasonLate
        );
    }

    private void _Drop(string? requestId, string reason)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.ReplyDropped(ReplyProtocol.SanitizeRequestId(requestId), reason);
        }

        MessagingMetrics.RecordDroppedReply(reason);
    }

    private static string? _Normalize(string? tenantId)
    {
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
    }
}
