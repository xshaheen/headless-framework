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
    IPublishExecutionPipeline publishPipeline
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
        return publishPipeline.ExecuteAsync(
            contentObj,
            options,
            delayTime: null,
            // DelayTime is undefined for the immediate publish path; ignored.
            innerPublish: (filteredOptions, _, ct) =>
                _PublishInternalAsync(publishRequestFactory.Create(contentObj, filteredOptions), ct),
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
            cancellationToken
        );
    }

    private async Task _PublishInternalAsync(PreparedPublishMessage publishRequest, CancellationToken cancellationToken)
    {
        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBefore(publishRequest.Message);

            var currentTransaction = transactionAccessor.Current;
            if (currentTransaction?.DbTransaction == null)
            {
                var mediumMessage = await storage
                    .StoreMessageAsync(publishRequest.Topic, publishRequest.Message)
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message);

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

                _TracingAfter(tracingTimestamp, publishRequest.Message);

                transaction.AddToSent(mediumMessage);

                if (currentTransaction.AutoCommit)
                {
                    await currentTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            _TracingError(tracingTimestamp, publishRequest.Message, e);

            throw;
        }
    }

    #region tracing

    private static long? _TracingBefore(Message message)
    {
        if (DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, Message message)
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublishMessageStore)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublishMessageStore, eventData);
        }
    }

    private static void _TracingError(long? tracingTimestamp, Message message, Exception ex)
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    #endregion
}
