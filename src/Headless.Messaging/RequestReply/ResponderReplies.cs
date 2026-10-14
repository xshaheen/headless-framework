// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Builds a responder host's reply to a request and sends it through <see cref="ReplySender"/>. A reply is the answer to
/// one caller, not a lane message: it skips the publish pipeline and the outbox, carries the response contract from this
/// host's registry, and is sent at most once and never retried.
/// </summary>
/// <remarks>
/// Neither method throws: a reply is sent after the request's outcome is durable, so a failure to build or send it can
/// never cost the consumed message. A response the host cannot build, or one the transport refuses, is replaced by a
/// <see cref="RequestFaultCodes.ResponseNotSent"/> fault, so the caller learns the work ran instead of waiting out its
/// timeout. A send that times out is not replaced, because the broker may have taken it.
/// </remarks>
internal sealed class ResponderReplies(
    IServiceProvider services,
    IMessageSerializer serializer,
    IMessagePublishRequestFactory contracts,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> options,
    ILogger<ResponderReplies> logger
)
{
    // A host whose transport cannot reply never resolves a responder, but a plain consumer on it can still be reached by
    // a request; such a host drops the fault it cannot send.
    private readonly Lazy<ReplySender?> _sender = new(() =>
        services.GetService<IReplyTransport>() is null ? null : services.GetRequiredService<ReplySender>()
    );

    /// <summary>The fault code a consume failure maps to: what the caller can conclude about the work.</summary>
    public static string FaultCodeFor(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case ResponderNullResponseException:
                    return RequestFaultCodes.NullResponse;
                case SubscriberNotFoundException:
                    return RequestFaultCodes.NoResponder;
                case MessageDeserializationException:
                    return RequestFaultCodes.RequestRejected;
            }
        }

        return RequestFaultCodes.HandlerFailed;
    }

    /// <summary>Sends the responder's <paramref name="response"/> to the caller of <paramref name="request"/>.</summary>
    /// <param name="request">The request envelope the responder handled.</param>
    /// <param name="response">The value the responder returned.</param>
    /// <param name="responseType">The responder's declared response type, which names the reply's contract.</param>
    /// <param name="responseHeaders">
    /// The headers the responder set, which <see cref="ConsumeContext.SetResponseHeader"/> already kept clear of the
    /// framework's own; the framework headers are written after them all the same.
    /// </param>
    /// <param name="responderSpan">The responder's span, so the caller's trace continues from the work that answered.</param>
    public async ValueTask SendResponseAsync(
        Message request,
        object response,
        Type responseType,
        IDictionary<string, string?>? responseHeaders,
        ActivityContext responderSpan
    )
    {
        Exception? buildFailure = null;
        try
        {
            var headers = _CreateHeaders(request, ReplyProtocol.StatusOk, responderSpan, responseHeaders);
            var (name, version) = contracts.ResolveContract(responseType, MessageLane.Queue);
            headers[Headers.MessageName] = name;
            headers[Headers.ContractVersion] = version;

            var reply = await serializer
                .SerializeToTransportMessageAsync(new Message(headers, response))
                .ConfigureAwait(false);

            if (await _SendAsync(request, reply).ConfigureAwait(false) is not ReplySendOutcome.Failed)
            {
                return;
            }
        }
        catch (Exception e)
        {
            logger.ReplyBuildFailed(e, _RequestId(request));
            buildFailure = e;
        }

        // A fault is small and carries no responder headers, so it usually gets through where the response did not.
        await SendFaultAsync(request, RequestFaultCodes.ResponseNotSent, buildFailure).ConfigureAwait(false);
    }

    /// <summary>Sends a fault with <paramref name="code"/> to the caller of <paramref name="request"/>.</summary>
    /// <param name="request">The request envelope that failed.</param>
    /// <param name="code">One of <see cref="RequestFaultCodes"/>.</param>
    /// <param name="exception">
    /// The failure, whose type and message reach the caller only when the host opts in through
    /// <see cref="RequestReplyOptions.IncludeExceptionDetailsInFaults"/>.
    /// </param>
    public async ValueTask SendFaultAsync(Message request, string code, Exception? exception)
    {
        try
        {
            var fault = new ReplyFault(code);
            if (options.Value.RequestReply.IncludeExceptionDetailsInFaults && exception is not null)
            {
                var cause = exception is SubscriberExecutionFailedException { InnerException: { } inner }
                    ? inner
                    : exception;
                fault = fault with
                {
                    ExceptionType = cause.GetType().FullName ?? cause.GetType().Name,
                    Detail = LogSanitizer.Sanitize(cause.Message, ReplyProtocol.MaxFaultDetailLength),
                };
            }

            var headers = _CreateHeaders(request, ReplyProtocol.StatusFault, Activity.Current?.Context ?? default);
            _ = await _SendAsync(request, new TransportMessage(headers, ReplyProtocol.WriteFault(fault)))
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.ReplyBuildFailed(e, _RequestId(request));
        }
    }

    /// <summary>Sends <paramref name="reply"/> to the address <paramref name="request"/> asked for, bounded in time.</summary>
    /// <returns>
    /// What the sender reported, or <see langword="null"/> when the host has no reply transport or the send timed out.
    /// </returns>
    private async ValueTask<ReplySendOutcome?> _SendAsync(Message request, TransportMessage reply)
    {
        if (_sender.Value is not { } sender)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.ReplyDropped(_RequestId(request), "no reply transport");
            }

            return null;
        }

        request.Headers.TryGetValue(Headers.ReplyTo, out var address);

        // The outcome the reply reports is already durable, so host shutdown must not abandon the send halfway. The
        // send is still bounded like any direct publish: a stalled broker must not hold the consumer that awaits it.
        using var sendTimeout = new CancellationTokenSource(options.Value.TransportPublishTimeout, timeProvider);
        try
        {
            return await sender.SendAsync(address, reply, sendTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (sendTimeout.IsCancellationRequested)
        {
            logger.ReplySendFailed(e, _RequestId(request));
            return null;
        }
    }

    private Dictionary<string, string?> _CreateHeaders(
        Message request,
        string status,
        ActivityContext span,
        IDictionary<string, string?>? responseHeaders = null
    )
    {
        request.Headers.TryGetValue(Headers.MessageId, out var requestMessageId);
        request.Headers.TryGetValue(Headers.CorrelationId, out var correlationId);

        var headers = responseHeaders is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(responseHeaders, StringComparer.Ordinal);

        headers[Headers.MessageId] = Guid.CreateVersion7(timeProvider.GetUtcNow()).ToString("D");
        headers[Headers.InReplyTo] = request.Headers.TryGetValue(Headers.RequestId, out var requestId)
            ? requestId
            : null;
        headers[Headers.ReplyStatus] = status;
        headers[Headers.CorrelationId] = string.IsNullOrWhiteSpace(correlationId) ? requestMessageId : correlationId;
        headers[Headers.CausationId] = requestMessageId;

        // The caller accepts only a reply under the tenant it sent the request under, so the reply carries the request's
        // tenant exactly as it arrived, whatever tenant is ambient here.
        if (request.Headers.TryGetValue(Headers.TenantId, out var tenantId) && tenantId is not null)
        {
            headers[Headers.TenantId] = tenantId;
        }

        if (span != default)
        {
            MessagingTelemetry.InjectTraceContext(span, headers);
        }
        else if (request.Headers.TryGetValue(Headers.TraceParent, out var traceParent) && traceParent is not null)
        {
            headers[Headers.TraceParent] = traceParent;
        }

        return headers;
    }

    private static string? _RequestId(Message request)
    {
        request.Headers.TryGetValue(Headers.RequestId, out var requestId);
        return ReplyProtocol.SanitizeRequestId(requestId);
    }
}
