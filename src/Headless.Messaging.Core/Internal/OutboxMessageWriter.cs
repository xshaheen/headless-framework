// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Diagnostics;
using Headless.CommitCoordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal sealed class OutboxMessageWriter(
    IDataStorage storage,
    IDispatcher dispatcher,
    IMessagePublishRequestFactory publishRequestFactory,
    ICurrentCommitCoordinator currentCommitCoordinator,
    IPublishMiddlewarePipeline publishPipeline,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> messagingOptions,
    ILogger<MessageOutboxBuffer> outboxBufferLogger
)
{
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
        // Capture the ambient coordinator + relational transaction ONCE here, in the caller's frame, before the
        // pipeline await. Re-reading ICurrentCommitCoordinator.Current inside _PublishInternalAsync (after the
        // middleware await) could observe a torn-down scope and silently fall through to the non-transactional
        // immediate path — dispatching to the broker non-atomically with the transaction.
        var coordinated = _TryCaptureCoordinatedContext();

        return publishPipeline.ExecuteAsync(
            contentObj,
            intentType,
            options,
            delayTime: null,
            // DelayTime is undefined for the immediate publish path; ignored.
            innerPublish: (middlewareOptions, _, ct) =>
                _PublishInternalAsync(
                    publishRequestFactory.Create(contentObj, middlewareOptions, intentType: intentType),
                    coordinated,
                    ct
                ),
            isTransactional: coordinated is not null,
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
        var coordinated = _TryCaptureCoordinatedContext();

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
                return _PublishInternalAsync(request, coordinated, ct);
            },
            isTransactional: coordinated is not null,
            cancellationToken
        );
    }

    private CoordinatedPublishContext? _TryCaptureCoordinatedContext()
    {
        var coordinator = currentCommitCoordinator.Current;

        if (
            coordinator?.TryGetCapability<IRelationalCommitContext>(out var relationalCommitContext) == true
            && relationalCommitContext.Transaction is { } transaction
        )
        {
            return new CoordinatedPublishContext(coordinator, transaction);
        }

        return null;
    }

    private async Task _PublishInternalAsync(
        PreparedPublishMessage publishRequest,
        CoordinatedPublishContext? coordinated,
        CancellationToken cancellationToken
    )
    {
        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBefore(publishRequest.Message, publishRequest.IntentType, cancellationToken);

            // Use the coordinator/transaction captured in the caller's frame — never re-read Current here. If the
            // captured transaction has since completed, StoreMessageAsync fails loudly rather than silently dropping
            // to the non-atomic immediate path.
            if (coordinated is { } context)
            {
                var mediumMessage = await storage
                    .StoreMessageAsync(
                        publishRequest.MessageName,
                        _CreateStorageEnvelope(publishRequest),
                        context.Transaction,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, publishRequest.Message, publishRequest.IntentType, cancellationToken);

                var bufferState = new MessageOutboxBufferState(
                    dispatcher,
                    messagingOptions.Value.OutboxFlushTimeout,
                    timeProvider,
                    outboxBufferLogger
                );
                var buffer = context.Coordinator.GetOrAdd(
                    bufferState,
                    static (coordinator, state) =>
                        new MessageOutboxBuffer(
                            coordinator,
                            state.Dispatcher,
                            state.FlushTimeout,
                            state.TimeProvider,
                            state.Logger
                        )
                );
                buffer.Add(mediumMessage);

                return;
            }

            // No ambient coordinator (or no relational transaction on it): store immediately with no transaction
            // and dispatch in-band. The message is persisted and enqueued in one shot — no atomic enlistment.
            var immediateMessage = await storage
                .StoreMessageAsync(
                    publishRequest.MessageName,
                    _CreateStorageEnvelope(publishRequest),
                    transaction: null,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, publishRequest.Message, publishRequest.IntentType, cancellationToken);

            if (publishRequest.Message.Headers.ContainsKey(Headers.DelayTime))
            {
                await dispatcher
                    .EnqueueToScheduler(
                        immediateMessage,
                        publishRequest.PublishAt,
                        transaction: null,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                await dispatcher.EnqueueToPublish(immediateMessage, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            _TracingError(tracingTimestamp, publishRequest.Message, publishRequest.IntentType, e, cancellationToken);

            throw;
        }
    }

    private readonly record struct MessageOutboxBufferState(
        IDispatcher Dispatcher,
        TimeSpan FlushTimeout,
        TimeProvider TimeProvider,
        ILogger<MessageOutboxBuffer> Logger
    );

    // The ambient coordinator + relational transaction captured once at publish time, carried through the pipeline so
    // the inner publish never re-reads the AsyncLocal Current after an await.
    private readonly record struct CoordinatedPublishContext(ICommitCoordinator Coordinator, DbTransaction Transaction);

    private static MediumMessage _CreateStorageEnvelope(PreparedPublishMessage publishRequest)
    {
        return new()
        {
            StorageId = Guid.Empty,
            Origin = publishRequest.Message,
            Content = string.Empty,
            IntentType = publishRequest.IntentType,
        };
    }

    #region Tracing

    private long? _TracingBefore(Message message, IntentType intentType, CancellationToken cancellationToken)
    {
        if (DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = _NowUnixTimeMilliseconds(),
                Operation = message.Name,
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
                Operation = message.Name,
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
                Operation = message.Name,
                Message = message,
                IntentType = intentType,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
                CancellationToken = cancellationToken,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    private long _NowUnixTimeMilliseconds()
    {
        return timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
    }

    #endregion
}
