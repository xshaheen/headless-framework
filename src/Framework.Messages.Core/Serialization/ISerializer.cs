// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Framework.Messages.Messages;

namespace Framework.Messages.Serialization;

public interface ISerializer
{
    /// <summary>
    /// Serializes the given <see cref="Message" /> into a string
    /// </summary>
    string Serialize(Message message);

    /// <summary>
    /// Serializes the given <see cref="Message" /> into a <see cref="TransportMessage" />
    /// </summary>
    ValueTask<TransportMessage> SerializeToTransportMessageAsync(Message message);

    /// <summary>
    /// Deserialize the given string into a <see cref="Message" />
    /// </summary>
    Message? Deserialize(string json);

    /// <summary>
    /// Deserialize the given <see cref="TransportMessage" /> back into a <see cref="Message" />
    /// </summary>
    ValueTask<Message> DeserializeAsync(TransportMessage transportMessage, Type? valueType);

    /// <summary>
    /// Deserialize the given object with the given Type into an object
    /// </summary>
    object? Deserialize(object value, Type valueType);

    /// <summary>
    /// Check if the given object is of Json type, e.g. JToken or JsonElement
    /// depending on the type of serializer implemented
    /// </summary>
    /// <example>
    ///     <code>
    /// // Example implementation for System.Text.Json
    /// public bool IsJsonType(object jsonObject)
    /// {
    ///    return jsonObject is JsonElement;
    /// }
    /// </code>
    /// </example>
    bool IsJsonType(object jsonObject);
}
