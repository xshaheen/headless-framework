// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Configures a publish operation with explicit topic, correlation, and custom header overrides.
/// </summary>
public sealed class PublishOptions
{
    /// <summary>
    /// Gets the explicit topic override. When <see langword="null"/>, the topic is resolved from mappings or conventions.
    /// </summary>
    public string? Topic { get; init; }

    /// <summary>
    /// Gets custom application headers. Reserved messaging headers are rejected.
    /// </summary>
    public IDictionary<string, string?>? Headers { get; init; }

    /// <summary>
    /// Gets the explicit logical message identifier override.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Gets the explicit correlation identifier override.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the explicit correlation sequence override.
    /// </summary>
    public int? CorrelationSequence { get; init; }

    /// <summary>
    /// Gets the callback topic override used for response messages.
    /// </summary>
    public string? CallbackName { get; init; }
}
