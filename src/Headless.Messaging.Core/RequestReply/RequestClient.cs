// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Sends a request through the Queue publish path and awaits its one outcome through the host's reply listener.
/// </summary>
internal sealed class RequestClient(
    MessagePublisher publisher,
    IMessagePublishRequestFactory publishRequestFactory,
    ReplyListenerHost listener,
    PendingRequests pending,
    IConsumeContextAccessor consumeContextAccessor,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> messagingOptions
) : IRequestClient
{
    private readonly TimeSpan _defaultTimeout = messagingOptions.Value.RequestReply.DefaultTimeout;

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default
    )
        where TRequest : class
        where TResponse : class
    {
        Argument.IsNotNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var inbound = consumeContextAccessor.Current;
        if (inbound?.UnitOfWork is not null)
        {
            throw new InvalidOperationException(
                "A request cannot be sent from inside a transactional inbox unit: waiting for the reply would hold the "
                    + "database transaction and the inbox lease for the whole timeout. Send it after the unit completes, "
                    + "or from a consumer on the non-transactional tier."
            );
        }

        var startedAt = timeProvider.GetTimestamp();
        var outcome = MessagingMetrics.RequestOutcomeFailed;

        try
        {
            // One clock read serves both the nested cap and the outbound deadline, so a nested request's deadline never
            // passes the deadline of the request its consumer is answering.
            var sentAt = timeProvider.GetUtcNow();
            var timeout = _ResolveTimeout(options, inbound, sentAt);
            var response = await _RequestAsync<TRequest, TResponse>(
                    request,
                    options,
                    timeout,
                    sentAt,
                    startedAt,
                    cancellationToken
                )
                .ConfigureAwait(false);
            outcome = MessagingMetrics.RequestOutcomeReplied;
            return response;
        }
        catch (Exception e)
        {
            outcome = _ClassifyFailure(e, cancellationToken);
            throw;
        }
        finally
        {
            MessagingMetrics.RecordRequest(outcome, timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    /// <summary>
    /// The call's timeout: the requested or default one, capped by what is left of the deadline of the request the
    /// current consumer is answering, so downstream work and its retries never outlive the caller waiting upstream.
    /// </summary>
    /// <exception cref="RequestNotSentException">The inbound request's deadline has already passed.</exception>
    private TimeSpan _ResolveTimeout(RequestOptions? options, ConsumeContext? inbound, DateTimeOffset now)
    {
        var timeout = options?.Timeout ?? _defaultTimeout;

        // Only a request being answered carries a deadline to inherit; a plain message, a Bus message with request
        // headers, or a request with an unreadable deadline leaves the timeout as asked.
        if (
            inbound is null
            || !RequestEnvelope.IsRequest(inbound.Lane, inbound.Headers)
            || RequestEnvelope.GetDeadline(inbound.Headers) is not { } inboundDeadline
        )
        {
            return timeout;
        }

        var remaining = inboundDeadline - now;
        if (remaining <= TimeSpan.Zero)
        {
            throw new RequestNotSentException(
                "The request this consumer is answering passed its deadline "
                    + $"{inboundDeadline.ToString("O", CultureInfo.InvariantCulture)}, so the nested request was not sent."
            );
        }

        return remaining < timeout ? remaining : timeout;
    }

    private async Task<TResponse> _RequestAsync<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options,
        TimeSpan timeout,
        DateTimeOffset sentAt,
        long startedAt,
        CancellationToken cancellationToken
    )
        where TRequest : class
        where TResponse : class
    {
        var (expectedName, expectedVersion) = publishRequestFactory.ResolveContract(
            typeof(TResponse),
            MessageLane.Queue
        );
        var replyTo = await _WaitForAddressAsync(timeout, cancellationToken).ConfigureAwait(false);

        // The readiness wait spends the same budget, so the reply wait gets only what is left of it.
        var remaining = timeout - timeProvider.GetElapsedTime(startedAt);
        if (remaining <= TimeSpan.Zero)
        {
            throw new RequestNotSentException($"The reply listener did not become ready within {timeout}.");
        }

        var requestId = Guid.CreateVersion7(sentAt).ToString("D");
        var call = new PendingRequest(
            requestId,
            typeof(TResponse),
            expectedName,
            expectedVersion,
            timeout,
            timeProvider
        );

        if (!pending.TryRegister(call, remaining, cancellationToken))
        {
            throw ReplyListenerHost.Stopping(requestId);
        }

        var stamp = new RequestStamp(
            requestId,
            replyTo,
            _ComputeDeadline(sentAt, timeout),
            tenantId =>
            {
                // Shutdown can begin while publish middleware runs; a request that has not reached the transport yet
                // is held back rather than sent for a call that already failed.
                if (pending.IsClosed)
                {
                    throw ReplyListenerHost.Stopping(requestId);
                }

                call.Prepare(tenantId);
            }
        );

        PublishReceipt receipt;
        try
        {
            receipt = await publisher
                .PublishRequestAsync(request, _CreateQueueOptions(options), stamp, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && call.IsPrepared)
        {
            // The transport publish timed out, not the caller: the broker may have taken the request, so its reply can
            // still arrive. The call stays armed and ends with that reply or with its own timeout.
            return (TResponse)await call.Outcome.ConfigureAwait(false);
        }
        catch (PublisherSentFailedException e)
        {
            pending.Discard(call);
            throw new RequestNotSentException(
                "The transport reported that the request could not be sent.",
                requestId,
                e
            );
        }
        catch
        {
            pending.Discard(call);
            throw;
        }

        if (receipt.MessageId is null)
        {
            pending.Discard(call);
            throw new RequestNotSentException(
                "Publish middleware suppressed the request, so it was not sent.",
                requestId
            );
        }

        return (TResponse)await call.Outcome.ConfigureAwait(false);
    }

    private async Task<string> _WaitForAddressAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var readiness = new CancellationTokenSource(PendingRequest.ClampTimerDuration(timeout), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readiness.Token);

        try
        {
            return await listener.WaitForAddressAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RequestNotSentException(
                $"The reply listener did not become ready within {timeout}, so the request was not sent.",
                requestId: null,
                e
            );
        }
    }

    // Only the caller's explicit tenant is set here; the Queue-lane publish middleware stamps the ambient tenant when the
    // host propagates tenants, and the final envelope's tenant reaches the pending call through the request stamp.
    private static QueueOptions _CreateQueueOptions(RequestOptions? options)
    {
        return new QueueOptions
        {
            DeliveryMode = DeliveryMode.Direct,
            TenantId = options?.TenantId,
            CorrelationId = options?.CorrelationId,
            Headers = options?.Headers,
        };
    }

    // RequestOptions.Timeout has no upper bound, so the deadline saturates instead of overflowing.
    private static DateTimeOffset _ComputeDeadline(DateTimeOffset sentAt, TimeSpan timeout)
    {
        return timeout >= DateTimeOffset.MaxValue - sentAt ? DateTimeOffset.MaxValue : sentAt + timeout;
    }

    private static string _ClassifyFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            RequestTimeoutException => MessagingMetrics.RequestOutcomeTimedOut,
            RequestFaultedException => MessagingMetrics.RequestOutcomeFaulted,
            ResponseContractMismatchException => MessagingMetrics.RequestOutcomeContractMismatch,
            RequestAbortedException => MessagingMetrics.RequestOutcomeAborted,
            RequestNotSentException => MessagingMetrics.RequestOutcomeNotSent,
            OperationCanceledException when cancellationToken.IsCancellationRequested =>
                MessagingMetrics.RequestOutcomeCanceled,
            _ => MessagingMetrics.RequestOutcomeFailed,
        };
    }
}
