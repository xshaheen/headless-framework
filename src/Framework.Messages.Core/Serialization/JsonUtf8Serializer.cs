// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Serialization;

public sealed class JsonUtf8Serializer(IOptions<CapOptions> capOptions) : ISerializer
{
    private readonly JsonSerializerOptions _jsonOptions = capOptions.Value.JsonSerializerOptions;

    public ValueTask<TransportMessage> SerializeToTransportMessageAsync(Message message)
    {
        Argument.IsNotNull(message);

        if (message.Value == null)
        {
            return new ValueTask<TransportMessage>(new TransportMessage(message.Headers, body: null));
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(message.Value, _jsonOptions);

        return new ValueTask<TransportMessage>(new TransportMessage(message.Headers, jsonBytes));
    }

    public ValueTask<Message> DeserializeAsync(TransportMessage transportMessage, Type? valueType)
    {
        if (valueType == null || transportMessage.Body.Length == 0)
        {
            return new ValueTask<Message>(new Message(transportMessage.Headers, value: null));
        }

        var obj = JsonSerializer.Deserialize(transportMessage.Body.Span, valueType, _jsonOptions);

        return new ValueTask<Message>(new Message(transportMessage.Headers, obj));
    }

    public string Serialize(Message message)
    {
        return JsonSerializer.Serialize(message, _jsonOptions);
    }

    public Message? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<Message>(json, _jsonOptions);
    }

    public object? Deserialize(object value, Type valueType)
    {
        return value is JsonElement jsonElement
            ? jsonElement.Deserialize(valueType, _jsonOptions)
            : throw new NotSupportedException("Type is not of type JsonElement");
    }

    public bool IsJsonType(object jsonObject)
    {
        return jsonObject is JsonElement;
    }
}
