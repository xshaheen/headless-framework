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
    IOutboxTransactionAccessor transactionAccessor
) : IOutboxPublisher
{
    // ReSharper disable once InconsistentNaming
    private static DiagnosticListener DiagnosticListener { get; } =
        new(MessageDiagnosticListenerNames.DiagnosticListenerName);

    private readonly IDataStorage _storage = storage;
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IMessagePublishRequestFactory _publishRequestFactory = publishRequestFactory;
    private readonly IOutboxTransactionAccessor _transactionAccessor = transactionAccessor;

    public Task PublishAsync<T>(T? contentObj, PublishOptions? options = null, CancellationToken cancellationToken = default)
    {
        return _PublishInternalAsync(_publishRequestFactory.Create(contentObj, options), cancellationToken);
    }

    public Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        PublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        return _PublishInternalAsync(_publishRequestFactory.Create(contentObj, options, delayTime), cancellationToken);
    }

    private async Task _PublishInternalAsync(
        PreparedPublishMessage publishRequest,
        CancellationToken cancellationToken
    )
    {
        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBefore(publishRequest.Message);

            var currentTransaction = _transactionAccessor.Current;
            if (currentTransaction?.DbTransaction == null)
            {
                var mediumMessage = await _storage
                    .StoreMessageAsync(publishRequest.Topic, publishRequest.Message)
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message);

                if (publishRequest.Message.Headers.ContainsKey(Headers.DelayTime))
                {
                    await _dispatcher
                        .EnqueueToScheduler(mediumMessage, publishRequest.PublishAt, null, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _dispatcher.EnqueueToPublish(mediumMessage, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (currentTransaction is not OutboxTransaction transaction)
                {
                    throw new InvalidOperationException(
                        $"Registered {nameof(IOutboxTransaction)} must derive from {nameof(OutboxTransaction)}."
                    );
                }

                var mediumMessage = await _storage
                    .StoreMessageAsync(publishRequest.Topic, publishRequest.Message, transaction.DbTransaction)
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message);

                transaction.AddToSent(mediumMessage);

                if (transaction.AutoCommit)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
