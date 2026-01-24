// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Messages;

/// <summary>
/// Represents a message in the messaging system, containing metadata headers and the message content value.
/// Messages are the fundamental unit of communication in the publish-subscribe model.
/// </summary>
/// <remarks>
/// A message consists of:
/// <list type="bullet">
/// <item><description>Headers: Metadata about the message (ID, name, group, correlation tracking, etc.)</description></item>
/// <item><description>Value: The actual message payload that may be serialized to/from JSON</description></item>
/// </list>
/// The class requires a parameterless constructor and public property setters for System.Text.Json serialization support.
/// </remarks>
public sealed class Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Message"/> class with an empty headers dictionary.
    /// This constructor is required for System.Text.Json deserialization.
    /// </summary>
    public Message()
    {
        Headers = new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Message"/> class with specified headers and value.
    /// </summary>
    /// <param name="headers">
    /// A dictionary of message metadata headers (e.g., MessageId, MessageName, Group).
    /// This dictionary is used directly, not copied.
    /// </param>
    /// <param name="value">
    /// The message payload or content. Can be any serializable object or null.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="headers"/> is null.</exception>
    public Message(IDictionary<string, string?> headers, object? value)
    {
        Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        Value = value;
    }

    /// <summary>
    /// Gets or sets the message metadata headers as a dictionary of string key-value pairs.
    /// Headers contain system information (MessageId, MessageName, etc.) and custom application data.
    /// </summary>
    public IDictionary<string, string?> Headers { get; init; }

    /// <summary>
    /// Gets or sets the message payload or content.
    /// This is the actual data being transmitted and may be serialized as JSON or other formats.
    /// </summary>
    public object? Value { get; init; }
}

/// <summary>
/// Provides extension methods for working with <see cref="Message"/> objects,
/// including header access, metadata retrieval, and exception handling.
/// </summary>
public static class MessageExtensions
{
    extension(Message message)
    {
        /// <summary>
        /// Retrieves the unique message identifier from the message headers.
        /// </summary>
        /// <returns>The message ID stored in the <see cref="Headers.MessageId"/> header.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the MessageId header is not present.</exception>
        public string GetId()
        {
            return message.Headers[Headers.MessageId]!;
        }

        /// <summary>
        /// Retrieves the message name or topic from the message headers.
        /// The message name identifies the topic, exchange, or subject the message relates to.
        /// </summary>
        /// <returns>The message name stored in the <see cref="Headers.MessageName"/> header.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the MessageName header is not present.</exception>
        public string GetName()
        {
            return message.Headers[Headers.MessageName]!;
        }

        /// <summary>
        /// Attempts to retrieve the callback subscriber name from the message headers.
        /// The callback name identifies which subscriber should process the response.
        /// </summary>
        /// <returns>The callback subscriber name, or null if not specified in the headers.</returns>
        public string? GetCallbackName()
        {
            message.Headers.TryGetValue(Headers.CallbackName, out var value);
            return value;
        }

        /// <summary>
        /// Attempts to retrieve the consumer group name from the message headers.
        /// The group name identifies which subscriber group should consume the message.
        /// </summary>
        /// <returns>The consumer group name, or null if not specified in the headers.</returns>
        public string? GetGroup()
        {
            message.Headers.TryGetValue(Headers.Group, out var value);
            return value;
        }

        /// <summary>
        /// Retrieves the correlation sequence number from the message headers.
        /// This number is used for ordering and tracking correlated message sequences.
        /// </summary>
        /// <returns>
        /// The correlation sequence number, or 0 if the header is not present.
        /// </returns>
        public int GetCorrelationSequence()
        {
            return message.Headers.TryGetValue(Headers.CorrelationSequence, out var value)
                ? int.Parse(value!, CultureInfo.InvariantCulture)
                : 0;
        }

        /// <summary>
        /// Attempts to retrieve the execution instance ID from the message headers.
        /// This ID tracks which application instance executed the message processing.
        /// </summary>
        /// <returns>The execution instance ID, or null if not specified in the headers.</returns>
        public string? GetExecutionInstanceId()
        {
            message.Headers.TryGetValue(Headers.ExecutionInstanceId, out var value);
            return value;
        }

        /// <summary>
        /// Determines whether the message has an associated exception record.
        /// This indicates that processing of the message encountered an error.
        /// </summary>
        /// <returns>true if the message has an exception header; otherwise false.</returns>
        public bool HasException()
        {
            return message.Headers.ContainsKey(Headers.Exception);
        }

        /// <summary>
        /// Adds or updates the exception information in the message headers.
        /// The exception is formatted as "ExceptionTypeName-->ExceptionMessage".
        /// </summary>
        /// <param name="ex">The exception to record in the message.</param>
        public void AddOrUpdateException(Exception ex)
        {
            var msg = $"{ex.GetType().Name}-->{ex.Message}";

            message.Headers[Headers.Exception] = msg;
        }

        /// <summary>
        /// Removes the exception information from the message headers.
        /// Use this to clear error information after handling or retrying a failed message.
        /// </summary>
        public void RemoveException()
        {
            message.Headers.Remove(Headers.Exception);
        }
    }
}
