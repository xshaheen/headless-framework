// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using MsgHeaders = Headless.Messaging.Headers;

namespace Headless.Messaging.Testing.Internal;

/// <summary>Records Consumed and Faulted observations around the real consume pipeline.</summary>
/// <param name="inner">The pipeline being recorded.</param>
/// <param name="store">The observation store.</param>
/// <param name="awaitPublishedRecord">
/// Whether a recorded transport feeds this pipeline. When it does, each execution first waits for the message's
/// Published record: the in-memory transport hands a message to its consumer inside the send, before the sending
/// thread records Published, so without the wait a test could observe Consumed before Published for the same message.
/// </param>
/// <param name="publishedRecordTimeout">
/// Upper bound for that wait, <see cref="MessagingTestHarness.DefaultTimeout"/> when omitted; the consumer runs
/// regardless once it elapses.
/// </param>
internal sealed class RecordingConsumeMiddlewarePipeline(
    IConsumeMiddlewarePipeline inner,
    MessageObservationStore store,
    bool awaitPublishedRecord = false,
    TimeSpan? publishedRecordTimeout = null
) : IConsumeMiddlewarePipeline
{
    private readonly TimeSpan _publishedRecordTimeout = publishedRecordTimeout ?? MessagingTestHarness.DefaultTimeout;

    public Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        CancellationToken cancellationToken = default
    ) =>
        _ExecuteAndRecordAsync(
            context,
            messageInstance,
            messageType,
            () => inner.ExecuteAsync(context, messageInstance, messageType, cancellationToken),
            cancellationToken
        );

    public Task<ConsumerExecutedResult> ExecuteInScopeAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        IServiceProvider provider,
        CancellationToken cancellationToken = default
    ) =>
        _ExecuteAndRecordAsync(
            context,
            messageInstance,
            messageType,
            () => inner.ExecuteInScopeAsync(context, messageInstance, messageType, provider, cancellationToken),
            cancellationToken
        );

    private async Task<ConsumerExecutedResult> _ExecuteAndRecordAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        Func<Task<ConsumerExecutedResult>> execute,
        CancellationToken cancellationToken
    )
    {
        if (
            awaitPublishedRecord
            && context.MediumMessage.Origin.Headers.TryGetValue(MsgHeaders.MessageId, out var messageId)
            && !string.IsNullOrEmpty(messageId)
        )
        {
            await store
                .WaitForPublishedRecordAsync(messageId, _publishedRecordTimeout, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            var result = await execute().ConfigureAwait(false);
            store.Record(
                _CreateRecordedMessage(context, messageInstance, messageType),
                MessageObservationType.Consumed
            );
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            store.Record(
                _CreateRecordedMessage(context, messageInstance, messageType, ex),
                MessageObservationType.Faulted
            );
            throw;
        }
    }

    private RecordedMessage _CreateRecordedMessage(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        Exception? exception = null
    )
    {
        return RecordedMessage.FromHeaders(
            context.MediumMessage.Origin.Headers,
            messageInstance,
            messageType,
            store.GetUtcNow(),
            context.MediumMessage.Lane,
            exception
        );
    }
}
