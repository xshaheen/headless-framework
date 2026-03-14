// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    /// Gets or sets the application id used when deriving default consumer groups.
    /// </summary>
    public string ApplicationId { get; set; } = _ResolveDefaultApplicationId();

    /// <summary>
    /// Gets or sets the messaging version used when deriving default consumer groups.
    /// </summary>
    public string Version { get; set; } = "v1";

    /// <summary>
    /// Gets or sets an optional explicit default consumer group name.
    /// When set, it overrides convention-based group generation.
    /// </summary>
    public string? DefaultGroup { get; set; }

    /// <summary>
    /// Configures kebab-case topic generation.
    /// </summary>
    public MessagingConventions UseKebabCaseTopics()
    {
        TopicNaming = TopicNamingConvention.KebabCase;
        return this;
    }

    /// <summary>
    /// Configures type-name topic generation.
    /// </summary>
    public MessagingConventions UseTypeNameTopics()
    {
        TopicNaming = TopicNamingConvention.TypeName;
        return this;
    }

    /// <summary>
    /// Sets the application id used for deterministic default group generation.
    /// </summary>
    public MessagingConventions UseApplicationId(string applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            throw new ArgumentException("ApplicationId cannot be null or whitespace.", nameof(applicationId));
        }

        ApplicationId = applicationId;
        return this;
    }

    /// <summary>
    /// Sets the messaging version used for deterministic default group generation.
    /// </summary>
    public MessagingConventions UseVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version cannot be null or whitespace.", nameof(version));
        }

        Version = version;
        return this;
    }

    /// <summary>
    /// Sets a topic prefix.
    /// </summary>
    public MessagingConventions WithTopicPrefix(string? prefix)
    {
        TopicPrefix = prefix;
        return this;
    }

    /// <summary>
    /// Sets a topic suffix.
    /// </summary>
    public MessagingConventions WithTopicSuffix(string? suffix)
    {
        TopicSuffix = suffix;
        return this;
    }

    /// <summary>
    /// Sets an explicit default group name.
    /// </summary>
    public MessagingConventions WithDefaultGroup(string? defaultGroup)
    {
        DefaultGroup = defaultGroup;
        return this;
    }

    /// <summary>
    /// Generates a topic name for the specified message type based on the configured conventions.
    /// </summary>
    /// <param name="messageType">The message type to generate a topic name for.</param>
    /// <returns>The generated topic name.</returns>
    public string GetTopicName(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        var baseName = TopicNaming switch
        {
            TopicNamingConvention.KebabCase => ToKebabCase(messageType.Name),
            TopicNamingConvention.TypeName => messageType.Name,
            _ => messageType.Name,
        };

        return $"{TopicPrefix}{baseName}{TopicSuffix}";
    }

    /// <summary>
    /// Generates the default consumer group for the specified handler identity.
    /// </summary>
    public string GetGroupName(string handlerId)
    {
        if (!string.IsNullOrWhiteSpace(DefaultGroup))
        {
            return DefaultGroup!;
        }

        if (string.IsNullOrWhiteSpace(handlerId))
        {
            throw new ArgumentException("HandlerId cannot be null or whitespace.", nameof(handlerId));
        }

        var normalizedAppId = NormalizeSegment(ApplicationId);
        var normalizedHandlerId = NormalizeSegment(handlerId);
        var normalizedVersion = NormalizeSegment(Version);

        return $"{normalizedAppId}.{normalizedHandlerId}.{normalizedVersion}";
    }

    /// <summary>
    /// Creates the deterministic default handler identity for a closed <see cref="IConsume{TMessage}"/> registration.
    /// </summary>
    public static string GetDefaultHandlerId(Type consumerType, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(consumerType);
        ArgumentNullException.ThrowIfNull(messageType);

        var consumerName = consumerType.FullName ?? consumerType.Name;
        var messageName = messageType.FullName ?? messageType.Name;
        return $"{consumerName}|{messageName}";
    }

    /// <summary>
    /// Creates the deterministic default handler identity for a runtime function subscription.
    /// </summary>
    public static string GetDefaultRuntimeHandlerId(Type declaringType, string methodName, Type messageType)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(messageType);

        var declaringTypeName = declaringType.FullName ?? declaringType.Name;
        var messageName = messageType.FullName ?? messageType.Name;
        return $"{declaringTypeName}|{methodName}|{messageName}";
    }

    internal static string NormalizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                previousWasSeparator = false;
                continue;
            }

            if (c is '.' or '-' or '_')
            {
                if (!previousWasSeparator)
                {
                    builder.Append('.');
                    previousWasSeparator = true;
                }

                continue;
            }

            if (!previousWasSeparator)
            {
                builder.Append('.');
                previousWasSeparator = true;
            }
        }

        var normalized = builder.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (char.IsUpper(current))
            {
                var hasPrevious = i > 0;
                var previous = hasPrevious ? value[i - 1] : '\0';
                var hasNext = i + 1 < value.Length;
                var next = hasNext ? value[i + 1] : '\0';

                if (
                    hasPrevious
                    && (
                        char.IsLower(previous)
                        || char.IsDigit(previous)
                        || (char.IsUpper(previous) && hasNext && char.IsLower(next))
                    )
                )
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(current));
                continue;
            }

            if (current is '_' or ' ')
            {
                if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static string _ResolveDefaultApplicationId()
    {
        return AppDomain.CurrentDomain.FriendlyName;
    }
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
