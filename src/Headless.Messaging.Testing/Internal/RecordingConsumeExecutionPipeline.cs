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
    )
    {
        var originHeaders = context.MediumMessage.Origin.Headers;

        var messageId = originHeaders.TryGetValue(Headers.MessageId, out var id) ? id ?? string.Empty : string.Empty;
        var correlationId =
            originHeaders.TryGetValue(Headers.CorrelationId, out var corrId) && !string.IsNullOrWhiteSpace(corrId)
                ? corrId
                : null;
        var topic = originHeaders.TryGetValue(Headers.MessageName, out var name) ? name ?? string.Empty : string.Empty;

        return new RecordedMessage
        {
            MessageType = messageType,
            Message = messageInstance,
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = new Dictionary<string, string?>(originHeaders, StringComparer.Ordinal),
            Topic = topic,
            Timestamp = DateTimeOffset.UtcNow,
            Exception = exception,
        };
    }
}
