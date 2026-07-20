// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Serialization;

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
public interface ISerializer
{
    /// <summary>
    /// Serializes a <see cref="Message"/> envelope to a JSON string for storage in the outbox.
    /// </summary>
    /// <param name="message">The message envelope to serialize.</param>
    /// <returns>A JSON string representation of the message envelope.</returns>
    string Serialize(Message message);

    /// <summary>
    /// Serializes a <see cref="Message"/> envelope into a <see cref="TransportMessage"/> ready for broker dispatch.
    /// </summary>
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
    /// <param name="json">The JSON string produced by <see cref="Serialize"/>.</param>
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
