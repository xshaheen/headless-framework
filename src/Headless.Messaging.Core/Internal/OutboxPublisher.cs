// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

internal sealed class OutboxPublisher(
    IDataStorage storage,
    IDispatcher dispatcher,
    IMessagePublishRequestFactory publishRequestFactory,
    IOutboxTransactionAccessor transactionAccessor,
    IPublishExecutionPipeline publishPipeline,
    TimeProvider timeProvider
) : IOutboxPublisher, IScheduledPublisher
{
    // ReSharper disable once InconsistentNaming
    private static DiagnosticListener DiagnosticListener { get; } =
        new(MessageDiagnosticListenerNames.DiagnosticListenerName);

    public Task PublishAsync<T>(
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        // Pre-decide whether this publish lands on the non-AutoCommit transactional branch so the
        // pipeline can stamp PublishedContext.IsTransactional before the post-success filters run.
        var isTransactional = _IsNonAutoCommitTransactional();

        return publishPipeline.ExecuteAsync(
            contentObj,
            options,
            delayTime: null,
            // DelayTime is undefined for the immediate publish path; ignored.
            innerPublish: (filteredOptions, _, ct) =>
                _PublishInternalAsync(publishRequestFactory.Create(contentObj, filteredOptions), ct),
            isTransactional,
            cancellationToken
        );
    }

    public Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var isTransactional = _IsNonAutoCommitTransactional();

        return publishPipeline.ExecuteAsync(
            contentObj,
            options,
            delayTime,
            innerPublish: (filteredOptions, filteredDelay, ct) =>
            {
                // Filter mutated DelayTime to null → drop to immediate-publish path; otherwise use the
                // filter-mutated value, falling back to the caller-supplied delay if the filter left it untouched.
                var request = filteredDelay.HasValue
                    ? publishRequestFactory.Create(contentObj, filteredOptions, filteredDelay.Value)
                    : publishRequestFactory.Create(contentObj, filteredOptions);
                return _PublishInternalAsync(request, ct);
            },
            isTransactional,
            cancellationToken
        );
    }

    private bool _IsNonAutoCommitTransactional()
    {
        var currentTransaction = transactionAccessor.Current;
        return currentTransaction?.DbTransaction is not null && !currentTransaction.AutoCommit;
    }

    private async Task _PublishInternalAsync(PreparedPublishMessage publishRequest, CancellationToken cancellationToken)
    {
        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBefore(publishRequest.Message, cancellationToken);

            var currentTransaction = transactionAccessor.Current;
            if (currentTransaction?.DbTransaction == null)
            {
                var mediumMessage = await storage
                    .StoreMessageAsync(publishRequest.Topic, publishRequest.Message)
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message, cancellationToken);

                if (publishRequest.Message.Headers.ContainsKey(Headers.DelayTime))
                {
                    await dispatcher
                        .EnqueueToScheduler(mediumMessage, publishRequest.PublishAt, null, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await dispatcher.EnqueueToPublish(mediumMessage, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (currentTransaction is not IOutboxMessageBuffer transaction)
                {
                    throw new InvalidOperationException(
                        $"Registered {nameof(IOutboxTransaction)} must implement {nameof(IOutboxMessageBuffer)} "
                            + "when publishing with an ambient database transaction."
                    );
                }

                var mediumMessage = await storage
                    .StoreMessageAsync(publishRequest.Topic, publishRequest.Message, currentTransaction.DbTransaction)
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message, cancellationToken);

                transaction.AddToSent(mediumMessage);

                if (currentTransaction.AutoCommit)
                {
                    await currentTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            _TracingError(tracingTimestamp, publishRequest.Message, e, cancellationToken);

            throw;
        }
    }

    #region tracing

    private long? _TracingBefore(Message message, CancellationToken cancellationToken)
    {
        if (DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = _NowUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                CancellationToken = cancellationToken,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(long? tracingTimestamp, Message message, CancellationToken cancellationToken)
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublishMessageStore)
        )
        {
            var now = _NowUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                CancellationToken = cancellationToken,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublishMessageStore, eventData);
        }
    }

    private void _TracingError(
        long? tracingTimestamp,
        Message message,
        Exception ex,
        CancellationToken cancellationToken
    )
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore)
        )
        {
            var now = _NowUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
                CancellationToken = cancellationToken,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    private long _NowUnixTimeMilliseconds() => timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    #endregion
}
