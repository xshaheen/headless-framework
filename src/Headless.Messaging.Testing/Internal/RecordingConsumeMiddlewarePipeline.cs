// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
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
            _RecordUnlessCleared(context, messageInstance, messageType, generation, exception: null);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _RecordUnlessCleared(context, messageInstance, messageType, generation, ex);
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
        Type messageType,
        long generation,
        Exception? exception
    )
    {
        if (store.Generation != generation)
        {
            return;
        }

        store.Record(
            _CreateRecordedMessage(context, messageInstance, messageType, exception),
            exception is null ? MessageObservationType.Consumed : MessageObservationType.Faulted
        );
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
