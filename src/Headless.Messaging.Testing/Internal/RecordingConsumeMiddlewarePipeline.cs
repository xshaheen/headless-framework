// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Testing.Internal;

internal sealed class RecordingConsumeMiddlewarePipeline(
    IConsumeMiddlewarePipeline inner,
    MessageObservationStore store
) : IConsumeMiddlewarePipeline
{
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
            () => inner.ExecuteAsync(context, messageInstance, messageType, cancellationToken)
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
            () => inner.ExecuteInScopeAsync(context, messageInstance, messageType, provider, cancellationToken)
        );

    private async Task<ConsumerExecutedResult> _ExecuteAndRecordAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        Func<Task<ConsumerExecutedResult>> execute
    )
    {
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
