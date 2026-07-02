// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Configures convention-based message name naming and default consumer settings for messaging.
/// </summary>
[PublicAPI]
public sealed class MessagingConventions
{
    /// <summary>
    /// Gets or sets the message-name naming convention to use when generating message names from message types.
    /// Default is <see cref="MessageNamingConvention.TypeName"/>.
    /// </summary>
    public MessageNamingConvention MessageNaming { get; set; } = MessageNamingConvention.TypeName;

    /// <summary>
    /// Gets or sets an optional prefix to prepend to all generated message names.
    /// </summary>
    public string? MessageNamePrefix { get; set; }

    /// <summary>
    /// Gets or sets an optional suffix to append to all generated message names.
    /// </summary>
    public string? MessageNameSuffix { get; set; }

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
    /// Configures kebab-case message name generation.
    /// </summary>
    public MessagingConventions UseKebabCaseMessageNames()
    {
        MessageNaming = MessageNamingConvention.KebabCase;
        return this;
    }

    /// <summary>
    /// Configures type-name message name generation.
    /// </summary>
    public MessagingConventions UseTypeNameMessageNames()
    {
        MessageNaming = MessageNamingConvention.TypeName;
        return this;
    }

    /// <summary>
    /// Sets the application id used for deterministic default group generation.
    /// </summary>
    /// <param name="applicationId">A non-whitespace identifier that scopes generated group names to this application.</param>
    /// <returns>The same <see cref="MessagingConventions"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="applicationId"/> is null or whitespace.</exception>
    public MessagingConventions UseApplicationId(string applicationId)
    {
        ApplicationId = Argument.IsNotNullOrWhiteSpace(applicationId, "ApplicationId cannot be null or whitespace.");
        return this;
    }

    /// <summary>
    /// Sets the messaging version used for deterministic default group generation.
    /// </summary>
    /// <param name="version">A non-whitespace version label (e.g., <c>"v1"</c>, <c>"v2"</c>) appended to generated group names.</param>
    /// <returns>The same <see cref="MessagingConventions"/> instance for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="version"/> is null or whitespace.</exception>
    public MessagingConventions UseVersion(string version)
    {
        Version = Argument.IsNotNullOrWhiteSpace(version, "Version cannot be null or whitespace.");
        return this;
    }

    /// <summary>
    /// Sets a prefix that is prepended to every generated message name.
    /// </summary>
    /// <param name="prefix">The prefix string, or <see langword="null"/> to clear a previously set prefix.</param>
    /// <returns>The same <see cref="MessagingConventions"/> instance for chaining.</returns>
    public MessagingConventions WithMessageNamePrefix(string? prefix)
    {
        MessageNamePrefix = prefix;
        return this;
    }

    /// <summary>
    /// Sets a suffix that is appended to every generated message name.
    /// </summary>
    /// <param name="suffix">The suffix string, or <see langword="null"/> to clear a previously set suffix.</param>
    /// <returns>The same <see cref="MessagingConventions"/> instance for chaining.</returns>
    public MessagingConventions WithMessageNameSuffix(string? suffix)
    {
        MessageNameSuffix = suffix;
        return this;
    }

    /// <summary>
    /// Sets an explicit default consumer group name that overrides convention-based group generation.
    /// </summary>
    /// <param name="defaultGroup">The explicit group name, or <see langword="null"/> to restore convention-based generation.</param>
    /// <returns>The same <see cref="MessagingConventions"/> instance for chaining.</returns>
    public MessagingConventions WithDefaultGroup(string? defaultGroup)
    {
        DefaultGroup = defaultGroup;
        return this;
    }

    /// <summary>
    /// Generates a message name for the specified message type based on the configured conventions.
    /// </summary>
    /// <param name="messageType">The message type to generate a message name for.</param>
    /// <returns>The generated message name.</returns>
    public string GetMessageName(Type messageType)
    {
        Argument.IsNotNull(messageType);

        var baseName = MessageNaming switch
        {
            MessageNamingConvention.KebabCase => _ToKebabCase(messageType.Name),
            _ => messageType.Name,
        };

        return $"{MessageNamePrefix}{baseName}{MessageNameSuffix}";
    }

    /// <summary>
    /// Generates the default consumer group for the specified handler identity.
    /// </summary>
    /// <param name="handlerId">The deterministic handler identity string (e.g., from <see cref="GetDefaultHandlerId"/>).</param>
    /// <returns>
    /// The configured <see cref="DefaultGroup"/> when set; otherwise a dot-separated combination of the
    /// normalized application id, handler id, and messaging version.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="handlerId"/> is null or whitespace and no <see cref="DefaultGroup"/> is configured.</exception>
    public string GetGroupName(string handlerId)
    {
        if (!string.IsNullOrWhiteSpace(DefaultGroup))
        {
            return DefaultGroup;
        }

        Argument.IsNotNullOrWhiteSpace(handlerId, "HandlerId cannot be null or whitespace.");

        var normalizedAppId = NormalizeSegment(ApplicationId);
        var normalizedHandlerId = NormalizeSegment(handlerId);
        var normalizedVersion = NormalizeSegment(Version);

        return $"{normalizedAppId}.{normalizedHandlerId}.{normalizedVersion}";
    }

    /// <summary>
    /// Creates the deterministic default handler identity for a closed <see cref="IConsume{TMessage}"/> registration.
    /// </summary>
    /// <param name="consumerType">The concrete consumer type that implements <see cref="IConsume{TMessage}"/>.</param>
    /// <param name="messageType">The message type that the consumer handles.</param>
    /// <returns>A stable identity string in the form <c>{consumerFullName}|{messageFullName}</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="consumerType"/> or <paramref name="messageType"/> is <see langword="null"/>.</exception>
    public static string GetDefaultHandlerId(Type consumerType, Type messageType)
    {
        Argument.IsNotNull(consumerType);
        Argument.IsNotNull(messageType);

        var consumerName = consumerType.FullName ?? consumerType.Name;
        var messageName = messageType.FullName ?? messageType.Name;
        return $"{consumerName}|{messageName}";
    }

    /// <summary>
    /// Creates the deterministic default handler identity for a runtime delegate subscription.
    /// </summary>
    /// <param name="declaringType">The type that declares or owns the delegate method.</param>
    /// <param name="methodName">The method name of the delegate (use <c>nameof</c>).</param>
    /// <param name="messageType">The message type that the delegate handles.</param>
    /// <returns>A stable identity string in the form <c>{declaringTypeFullName}|{methodName}|{messageFullName}</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declaringType"/> or <paramref name="messageType"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="methodName"/> is null or whitespace.</exception>
    public static string GetDefaultRuntimeHandlerId(Type declaringType, string methodName, Type messageType)
    {
        Argument.IsNotNull(declaringType);
        Argument.IsNotNullOrWhiteSpace(methodName);
        Argument.IsNotNull(messageType);

        var declaringTypeName = declaringType.FullName ?? declaringType.Name;
        var messageName = messageType.FullName ?? messageType.Name;
        return $"{declaringTypeName}|{methodName}|{messageName}";
    }

    internal static string NormalizeSegment(string value)
    {
        Argument.IsNotNullOrWhiteSpace(value, "Value cannot be null or whitespace.");

        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                previousWasSeparator = false;
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

    private static string _ToKebabCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);

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
/// Defines the naming convention to use when generating message names from message types.
/// </summary>
[PublicAPI]
public enum MessageNamingConvention
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
