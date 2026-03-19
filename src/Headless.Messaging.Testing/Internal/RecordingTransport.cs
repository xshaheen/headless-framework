// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Testing.Internal;

internal sealed class RecordingTransport(ITransport inner, MessageObservationStore store, ISerializer serializer)
    : ITransport
{
    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        var result = await inner.SendAsync(message, cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            var headers = message.Headers;
            var messageId = headers.TryGetValue(Headers.MessageId, out var id) ? id ?? string.Empty : string.Empty;
            var correlationId =
                headers.TryGetValue(Headers.CorrelationId, out var corrId) && !string.IsNullOrWhiteSpace(corrId)
                    ? corrId
                    : null;
            var topic = headers.TryGetValue(Headers.MessageName, out var name) ? name ?? string.Empty : string.Empty;
            var messageTypeName = headers.TryGetValue(Headers.Type, out var typeName) ? typeName : null;

            object messageObj = message;
            Type messageType = typeof(TransportMessage);

            if (message.Body.Length > 0 && messageTypeName != null)
            {
                var resolvedType = Type.GetType(messageTypeName);

                if (resolvedType != null)
                {
                    try
                    {
                        var deserialized = await serializer
                            .DeserializeAsync(message, resolvedType)
                            .ConfigureAwait(false);

                        if (deserialized.Value != null)
                        {
                            messageObj = deserialized.Value;
                            messageType = resolvedType;
                        }
                    }
#pragma warning disable ERP022 // Intentional: deserialization failure is non-fatal; keep raw TransportMessage as the recorded payload
                    catch { }
#pragma warning restore ERP022
                }
            }

            var recorded = new RecordedMessage
            {
                MessageType = messageType,
                Message = messageObj,
                MessageId = messageId,
                CorrelationId = correlationId,
                Headers = new Dictionary<string, string?>(headers, StringComparer.Ordinal),
                Topic = topic,
                Timestamp = DateTimeOffset.UtcNow,
            };

            store.Record(recorded, MessageObservationType.Published);
        }

        return result;
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
