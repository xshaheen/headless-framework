// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.Messaging;

internal sealed class JsonUtf8Serializer(IOptions<MessagingOptions> messagingOptionsAccessor) : IMessageSerializer
{
    private readonly JsonSerializerOptions _jsonOptions = messagingOptionsAccessor.Value.JsonSerializerOptions;
    private bool? _canEmbedSerializedValue;

    public ValueTask<TransportMessage> SerializeToTransportMessageAsync(
        Message message,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(message);

        if (message.Value == null)
        {
            return new ValueTask<TransportMessage>(new TransportMessage(message.Headers, body: null));
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(message.Value, _jsonOptions);

        return new ValueTask<TransportMessage>(new TransportMessage(message.Headers, jsonBytes));
    }

    public ValueTask<Message> DeserializeAsync(
        TransportMessage transportMessage,
        Type? valueType,
        CancellationToken cancellationToken = default
    )
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

    public string Serialize(Message message, ReadOnlyMemory<byte> serializedValue)
    {
        Argument.IsNotNull(message);

        // A null value has no bytes to embed, and a body that is not one strict JSON value (comments or trailing
        // commas the options tolerated on read) must not reach the stored envelope verbatim.
        if (message.Value is null || !_IsSingleJsonValue(serializedValue.Span) || !_CanEmbedSerializedValue())
        {
            return Serialize(message);
        }

        // Serializing the envelope itself keeps every naming, encoder, and ignore setting of the options; only the
        // value is written from the bytes already in hand instead of from the object graph.
        return JsonSerializer.Serialize(
            new Message(message.Headers, new SerializedJsonValue(serializedValue)),
            _jsonOptions
        );
    }

    // A consumer's source-generated resolver may not know the wrapper type; such options keep the full serialization.
    private bool _CanEmbedSerializedValue()
    {
        return _canEmbedSerializedValue ??= _jsonOptions.TryGetTypeInfo(typeof(SerializedJsonValue), out _);
    }

    private static bool _IsSingleJsonValue(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            reader.Read();
            reader.Skip();

            // Reading past the value is the trailing-content check: a single-value reader throws on anything but the end.
            return !reader.Read();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>A payload already serialized to UTF-8 JSON, written into the envelope as it is.</summary>
    [JsonConverter(typeof(SerializedJsonValueConverter))]
    internal sealed class SerializedJsonValue(ReadOnlyMemory<byte> utf8Json)
    {
        public ReadOnlyMemory<byte> Utf8Json { get; } = utf8Json;
    }

    internal sealed class SerializedJsonValueConverter : JsonConverter<SerializedJsonValue>
    {
        public override SerializedJsonValue Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            throw new NotSupportedException("A serialized JSON value is written into an envelope, never read back.");
        }

        public override void Write(Utf8JsonWriter writer, SerializedJsonValue value, JsonSerializerOptions options)
        {
            // Validated by the caller before the envelope is serialized.
            writer.WriteRawValue(value.Utf8Json.Span, skipInputValidation: true);
        }
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
