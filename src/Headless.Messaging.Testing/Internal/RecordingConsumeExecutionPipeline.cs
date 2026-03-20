// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;

namespace Headless.Messaging.Testing.Internal;

internal sealed class RecordingConsumeExecutionPipeline(IConsumeExecutionPipeline inner, MessageObservationStore store)
    : IConsumeExecutionPipeline
{
    public async Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var result = await inner
                .ExecuteAsync(context, messageInstance, messageType, cancellationToken)
                .ConfigureAwait(false);
            store.Record(
                _CreateRecordedMessage(context, messageInstance, messageType),
                MessageObservationType.Consumed
            );
            return result;
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

    private static RecordedMessage _CreateRecordedMessage(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        Exception? exception = null
    ) => RecordedMessage.FromHeaders(context.MediumMessage.Origin.Headers, messageInstance, messageType, exception);
}
