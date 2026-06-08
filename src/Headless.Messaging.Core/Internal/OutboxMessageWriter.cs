// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Headless.AmbientTransactions;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

internal sealed class OutboxMessageWriter(
    IDataStorage storage,
    IDispatcher dispatcher,
    IMessagePublishRequestFactory publishRequestFactory,
    ICurrentAmbientTransaction currentAmbientTransaction,
    IEnumerable<IMessageOutboxBufferObserver> bufferObservers,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider
)
{
    private readonly ConditionalWeakTable<IAmbientTransaction, MessageOutboxBuffer> _buffers = new();

    // ReSharper disable once InconsistentNaming
    private static DiagnosticListener DiagnosticListener { get; } =
        new(MessageDiagnosticListenerNames.DiagnosticListenerName);

    internal Task PublishAsync<T>(
        T? contentObj,
        MessageOptions? options,
        IntentType intentType,
        CancellationToken cancellationToken
    )
    {
        // Pre-decide whether this publish lands on the non-AutoCommit transactional branch so the
        // pipeline can stamp PublishingContext.IsTransactional before post-success middleware resumes.
        var isTransactional = _IsNonAutoCommitTransactional();

        return publishPipeline.ExecuteAsync(
            contentObj,
            intentType,
            options,
            delayTime: null,
            // DelayTime is undefined for the immediate publish path; ignored.
            innerPublish: (middlewareOptions, _, ct) =>
                _PublishInternalAsync(
                    publishRequestFactory.Create(contentObj, middlewareOptions, intentType: intentType),
                    ct
                ),
            isTransactional,
            cancellationToken
        );
    }

    internal Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        MessageOptions? options,
        IntentType intentType,
        CancellationToken cancellationToken
    )
    {
        var isTransactional = _IsNonAutoCommitTransactional();

        return publishPipeline.ExecuteAsync(
            contentObj,
            intentType,
            options,
            delayTime,
            innerPublish: (middlewareOptions, middlewareDelay, ct) =>
            {
                // Middleware mutated DelayTime to null -> drop to immediate-publish path; otherwise use the
                // middleware-mutated value, falling back to the caller-supplied delay if middleware left it untouched.
                var request = middlewareDelay.HasValue
                    ? publishRequestFactory.Create(contentObj, middlewareOptions, middlewareDelay.Value, intentType)
                    : publishRequestFactory.Create(contentObj, middlewareOptions, intentType: intentType);
                return _PublishInternalAsync(request, ct);
            },
            isTransactional,
            cancellationToken
        );
    }

    private bool _IsNonAutoCommitTransactional()
    {
        var currentTransaction = currentAmbientTransaction.Current;
        return currentTransaction?.DbTransaction is not null && !currentTransaction.AutoCommit;
    }

    private async Task _PublishInternalAsync(PreparedPublishMessage publishRequest, CancellationToken cancellationToken)
    {
        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBefore(publishRequest.Message, publishRequest.IntentType, cancellationToken);

            var currentTransaction = currentAmbientTransaction.Current;

            if (currentTransaction?.DbTransaction == null)
            {
                var mediumMessage = await storage
                    .StoreMessageAsync(
                        publishRequest.MessageName,
                        _CreateStorageEnvelope(publishRequest),
                        null,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message, publishRequest.IntentType, cancellationToken);

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
                var mediumMessage = await storage
                    .StoreMessageAsync(
                        publishRequest.MessageName,
                        _CreateStorageEnvelope(publishRequest),
                        currentTransaction.DbTransaction,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message, publishRequest.IntentType, cancellationToken);

                var buffer = _buffers.GetValue(
                    currentTransaction,
                    transaction =>
                    {
                        var messageBuffer = new MessageOutboxBuffer(dispatcher);
                        transaction.RegisterCommitWork(messageBuffer.DrainAsync);
                        return messageBuffer;
                    }
                );

                buffer.Buffer(mediumMessage);

                foreach (var observer in bufferObservers)
                {
                    observer.MessageBuffered(currentTransaction, mediumMessage);
                }

                if (currentTransaction.AutoCommit)
                {
                    await currentTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            _TracingError(tracingTimestamp, publishRequest.Message, publishRequest.IntentType, e, cancellationToken);

            throw;
        }
    }

    private static MediumMessage _CreateStorageEnvelope(PreparedPublishMessage publishRequest) =>
        new()
        {
            StorageId = Guid.Empty,
            Origin = publishRequest.Message,
            Content = string.Empty,
            IntentType = publishRequest.IntentType,
        };

    #region Tracing

    private long? _TracingBefore(Message message, IntentType intentType, CancellationToken cancellationToken)
    {
        if (DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = _NowUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                IntentType = intentType,
                CancellationToken = cancellationToken,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(
        long? tracingTimestamp,
        Message message,
        IntentType intentType,
        CancellationToken cancellationToken
    )
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
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                CancellationToken = cancellationToken,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublishMessageStore, eventData);
        }
    }

    private void _TracingError(
        long? tracingTimestamp,
        Message message,
        IntentType intentType,
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
                IntentType = intentType,
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
