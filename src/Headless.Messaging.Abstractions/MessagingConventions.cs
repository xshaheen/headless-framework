// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Configures convention-based message naming for messaging.
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
    /// Creates the deterministic default handler identity for a runtime delegate subscription.
    /// </summary>
    /// <param name="declaringType">The type that declares or owns the delegate method.</param>
    /// <param name="methodName">The method name of the delegate (use <see langword="nameof"/>).</param>
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
    TypeName = 0,

    /// <summary>
    /// Convert to kebab-case (e.g., "order-created").
    /// </summary>
    KebabCase = 1,
}
