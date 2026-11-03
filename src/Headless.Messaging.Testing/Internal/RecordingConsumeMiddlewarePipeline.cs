// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Messaging.Internal;
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
        CancellationToken cancellationToken = default
    ) =>
        _ExecuteAndRecordAsync(
            context,
            messageInstance,
            () => inner.ExecuteAsync(context, messageInstance, cancellationToken),
            cancellationToken
        );

    public Task<ConsumerExecutedResult> ExecuteInScopeAsync(
        ConsumerContext context,
        object messageInstance,
        IServiceProvider provider,
        CancellationToken cancellationToken = default
    ) =>
        _ExecuteAndRecordAsync(
            context,
            messageInstance,
            () => inner.ExecuteInScopeAsync(context, messageInstance, provider, cancellationToken),
            cancellationToken
        );

    private async Task<ConsumerExecutedResult> _ExecuteAndRecordAsync(
        ConsumerContext context,
        object messageInstance,
        Func<Task<ConsumerExecutedResult>> execute,
        CancellationToken cancellationToken
    )
    {
        var headers = context.MediumMessage.Origin.Headers;
        var generation = store.Generation;

        if (_IsFromClearedRound(headers, generation))
        {
            // The transport handed this message over before a reset cleared its round: its Published record is gone,
            // so waiting for it would only hold the consumer thread, and recording it would leak a Consumed or
            // Faulted observation into the next test. The consumer itself still runs.
            return await execute().ConfigureAwait(false);
        }

        if (
            awaitPublishedRecord
            && headers.TryGetValue(MsgHeaders.MessageId, out var messageId)
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
            _RecordUnlessCleared(context, messageInstance, generation, exception: null);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _RecordUnlessCleared(context, messageInstance, generation, ex);
            throw;
        }
    }

    private static bool _IsFromClearedRound(IDictionary<string, string?> headers, long currentGeneration)
    {
        return headers.TryGetValue(RecordingHeaders.ResetGeneration, out var stamped)
            && long.TryParse(stamped, NumberStyles.None, CultureInfo.InvariantCulture, out var sentGeneration)
            && sentGeneration != currentGeneration;
    }

    // A reset that lands while the consumer runs moves the message into a cleared round as well.
    private void _RecordUnlessCleared(
        ConsumerContext context,
        object messageInstance,
        long generation,
        Exception? exception
    )
    {
        if (store.Generation != generation)
        {
            return;
        }

        store.Record(
            _CreateRecordedMessage(context, messageInstance, exception),
            exception is null ? MessageObservationType.Consumed : MessageObservationType.Faulted
        );
    }

    private RecordedMessage _CreateRecordedMessage(
        ConsumerContext context,
        object messageInstance,
        Exception? exception = null
    )
    {
        // Record the consumer's declared message type, the type the delivery was deserialized into, rather than the
        // instance's runtime type, so a handler of a base type sees that base type. The invoker refuses a descriptor
        // without one, so the runtime type only stands in for a caller that hands the pipeline such a descriptor.
        return RecordedMessage.FromHeaders(
            context.MediumMessage.Origin.Headers,
            messageInstance,
            context.ConsumerDescriptor.MessageType ?? messageInstance.GetType(),
            store.GetUtcNow(),
            context.MediumMessage.Lane,
            exception
        );
    }
}
