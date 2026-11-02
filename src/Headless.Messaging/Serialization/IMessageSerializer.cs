// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Converts messages between their in-memory representation and their serialized forms
/// (JSON string and <see cref="TransportMessage"/>).
/// </summary>
/// <remarks>
/// <para>
/// The default implementation uses <c>System.Text.Json</c>. Replace this contract in DI to
/// substitute a different serializer (e.g., MessagePack, Newtonsoft.Json).
/// </para>
/// <para>
/// <b>Evolution policy:</b> this is a consumer-replaceable extension point, so it evolves
/// additively. New members ship as default interface methods with a behavior-preserving
/// fallback; existing member signatures do not change within a major version. Custom
/// implementations therefore keep compiling across minor releases and may override new
/// defaults when they can do better.
/// </para>
/// </remarks>
[PublicAPI]
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes a <see cref="Message"/> envelope to a JSON string for storage in the outbox.
    /// </summary>
    /// <param name="message">The message envelope to serialize.</param>
    /// <returns>A JSON string representation of the message envelope.</returns>
    string Serialize(Message message);

    /// <summary>
    /// Serializes a <see cref="Message"/> envelope to the stored string form, reusing a payload already serialized
    /// for the transport instead of serializing <see cref="Message.Value"/> again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime calls this on durable publish with the <see cref="TransportMessage.Body"/> that
    /// <see cref="SerializeToTransportMessageAsync"/> returned for the same envelope, and on durable consume with the
    /// received body that <see cref="DeserializeAsync"/> turned into <see cref="Message.Value"/>. The result must
    /// read back through <see cref="Deserialize(string)"/> the same way <see cref="Serialize(Message)"/> output does.
    /// </para>
    /// <para>
    /// The default calls <see cref="Serialize(Message)"/>. Override it when the body can be embedded as it is, as
    /// the built-in System.Text.Json serializer does.
    /// </para>
    /// </remarks>
    /// <param name="message">The message envelope to serialize. Its headers are written as they are now.</param>
    /// <param name="serializedValue">
    /// The payload bytes produced from or deserialized into <see cref="Message.Value"/>; empty when there are none.
    /// </param>
    /// <returns>A string representation of the envelope, as <see cref="Serialize(Message)"/> would produce.</returns>
    string Serialize(Message message, ReadOnlyMemory<byte> serializedValue) => Serialize(message);

    /// <summary>
    /// Serializes a <see cref="Message"/> envelope into a <see cref="TransportMessage"/> ready for broker dispatch.
    /// </summary>
    /// <remarks>
    /// On durable publish the runtime calls this once, before the row is stored. When the returned
    /// <see cref="TransportMessage.Headers"/> is <paramref name="message"/>'s own <see cref="Message.Headers"/>
    /// dictionary, the first send of that same envelope instance reuses the result instead of calling this again, so
    /// header changes made after the store still reach the broker. A body produced that way must depend only on
    /// <see cref="Message.Value"/>. Return a copied header dictionary to have every send serialize afresh.
    /// </remarks>
    /// <param name="message">The message envelope to serialize.</param>
    /// <param name="cancellationToken">A token to observe while awaiting the serialization operation.</param>
    /// <returns>
    /// A <see cref="TransportMessage"/> with a headers dictionary and a UTF-8 encoded body.
    /// </returns>
    ValueTask<TransportMessage> SerializeToTransportMessageAsync(
        Message message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deserializes a JSON string back into a <see cref="Message"/> envelope.
    /// </summary>
    /// <param name="json">The JSON string produced by <see cref="Serialize(Message)"/>.</param>
    /// <returns>The deserialized envelope, or <see langword="null"/> when the input is empty or unrecognized.</returns>
    Message? Deserialize(string json);

    /// <summary>
    /// Deserializes a <see cref="TransportMessage"/> received from the broker back into a <see cref="Message"/> envelope.
    /// </summary>
    /// <param name="transportMessage">The transport message carrying the broker headers and body.</param>
    /// <param name="valueType">
    /// The expected CLR type of the message payload, or <see langword="null"/> to defer type
    /// resolution to the implementation.
    /// </param>
    /// <param name="cancellationToken">A token to observe while awaiting the deserialization operation.</param>
    /// <returns>The deserialized <see cref="Message"/> envelope.</returns>
    ValueTask<Message> DeserializeAsync(
        TransportMessage transportMessage,
        Type? valueType,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deserializes a raw value (e.g., a <c>JsonElement</c>) to the specified CLR type.
    /// </summary>
    /// <param name="value">The raw serialized value as produced by the underlying serializer.</param>
    /// <param name="valueType">The target CLR type to deserialize to.</param>
    /// <returns>The deserialized object, or <see langword="null"/> when the value cannot be converted.</returns>
    object? Deserialize(object value, Type valueType);

    /// <summary>
    /// Returns <see langword="true"/> when the given object is an intermediate JSON representation
    /// native to this serializer (e.g., <c>JsonElement</c> for System.Text.Json).
    /// </summary>
    /// <param name="jsonObject">The object to test.</param>
    /// <returns><see langword="true"/> when <paramref name="jsonObject"/> is a native JSON type for this serializer; otherwise <see langword="false"/>.</returns>
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
