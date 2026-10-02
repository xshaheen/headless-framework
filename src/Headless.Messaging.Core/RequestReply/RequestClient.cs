// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.MultiTenancy;
using Headless.MultiTenancy;
using Microsoft.Extensions.Logging;
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
    ICurrentTenant currentTenant,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> messagingOptions,
    IMiddlewareDescriptorRegistry middlewareDescriptors,
    ILogger<RequestClient> logger
) : IRequestClient
{
    private readonly TimeSpan _defaultTimeout = messagingOptions.Value.RequestReply.DefaultTimeout;

    // Tenant propagation is registered as Bus publish middleware only, so a request on the Queue lane reads the ambient
    // tenant itself, and only on a host that opted in to propagating tenants.
    private readonly bool _propagatesTenant = middlewareDescriptors.HasMiddleware<TenantPropagationPublishMiddleware>();

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

        if (consumeContextAccessor.Current?.UnitOfWork is not null)
        {
            throw new InvalidOperationException(
                "A request cannot be sent from inside a transactional inbox unit: waiting for the reply would hold the "
                    + "database transaction and the inbox lease for the whole timeout. Send it after the unit completes, "
                    + "or from a consumer on the non-transactional tier."
            );
        }

        var timeout = options?.Timeout ?? _defaultTimeout;
        var startedAt = timeProvider.GetTimestamp();
        var outcome = MessagingMetrics.RequestOutcomeFailed;

        try
        {
            var response = await _RequestAsync<TRequest, TResponse>(
                    request,
                    options,
                    timeout,
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

    private async Task<TResponse> _RequestAsync<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options,
        TimeSpan timeout,
        long startedAt,
        CancellationToken cancellationToken
    )
        where TRequest : class
        where TResponse : class
    {
        var sentAt = timeProvider.GetUtcNow();
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

        var stamp = new RequestStamp(requestId, replyTo, _ComputeDeadline(sentAt, timeout), call.Prepare);

        PublishReceipt receipt;
        try
        {
            receipt = await publisher
                .PublishRequestAsync(request, _CreateQueueOptions(options), stamp, cancellationToken)
                .ConfigureAwait(false);
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

    private QueueOptions _CreateQueueOptions(RequestOptions? options)
    {
        return new QueueOptions
        {
            DeliveryMode = DeliveryMode.Direct,
            TenantId =
                options?.TenantId
                ?? (
                    _propagatesTenant
                        ? TenantPropagationPublishMiddleware.ResolveAmbientTenant(currentTenant, logger)
                        : null
                ),
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
