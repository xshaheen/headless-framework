// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Headless.Messaging;

/// <summary>
/// Configures convention-based topic naming and default consumer settings for messaging.
/// </summary>
public sealed partial class MessagingConventions
{
    /// <summary>
    /// Gets or sets the topic naming convention to use when generating topic names from message types.
    /// Default is <see cref="TopicNamingConvention.TypeName"/>.
    /// </summary>
    public TopicNamingConvention TopicNaming { get; set; } = TopicNamingConvention.TypeName;

    /// <summary>
    /// Gets or sets an optional prefix to prepend to all generated topic names.
    /// </summary>
    public string? TopicPrefix { get; set; }

    /// <summary>
    /// Gets or sets an optional suffix to append to all generated topic names.
    /// </summary>
    public string? TopicSuffix { get; set; }

    /// <summary>
    /// Gets or sets the default consumer group name when not explicitly specified.
    /// </summary>
    public string? DefaultGroup { get; set; }

    /// <summary>
    /// Generates a topic name for the specified message type based on the configured conventions.
    /// </summary>
    /// <param name="messageType">The message type to generate a topic name for.</param>
    /// <returns>The generated topic name.</returns>
    public string GetTopicName(Type messageType)
    {
        var baseName = TopicNaming switch
        {
            TopicNamingConvention.KebabCase => _ToKebabCase(messageType.Name),
            TopicNamingConvention.TypeName => messageType.Name,
            _ => messageType.Name,
        };

        return $"{TopicPrefix}{baseName}{TopicSuffix}";
    }

    private static string _ToKebabCase(string value)
    {
        return string.IsNullOrEmpty(value) ? value : _KebabCaseRegex().Replace(value, "-$1").ToLowerInvariant();
    }

    [GeneratedRegex("(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])", RegexOptions.Compiled | RegexOptions.ExplicitCapture, 100)]
    private static partial Regex _KebabCaseRegex();
}

/// <summary>
/// Defines the naming convention to use when generating topic names from message types.
/// </summary>
public enum TopicNamingConvention
{
    /// <summary>
    /// Use the exact type name (e.g., "OrderCreated").
    /// </summary>
    TypeName,

    /// <summary>
    /// Convert to kebab-case (e.g., "order-created").
    /// </summary>
    KebabCase,
}
