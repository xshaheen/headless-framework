// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// The rules a message contract's name and version must meet. Declaration validates eagerly with them, and the messaging
/// runtime applies the same rules wherever a name or version reaches it.
/// </summary>
internal static class MessageContractRules
{
    /// <summary>Validates a logical message name: non-blank, at most 255 characters, letters, digits, dots, hyphens, and underscores.</summary>
    public static void ValidateMessageName(string messageName)
    {
        Argument.IsNotNullOrWhiteSpace(messageName);
        _ValidateMessageNameFormat(messageName);
    }

    /// <summary>Validates a contract schema version: non-blank, bounded, with no whitespace or control characters.</summary>
    public static string ValidateContractVersion(string contractVersion)
    {
        Argument.IsNotNullOrWhiteSpace(contractVersion);
        Argument.IsLessThanOrEqualTo(
            contractVersion.Length,
            MessageOptions.ContractVersionMaxLength,
            $"Message contract version must be {MessageOptions.ContractVersionMaxLength} characters or fewer.",
            nameof(contractVersion)
        );

        if (contractVersion.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Message contract version cannot contain control characters.",
                nameof(contractVersion)
            );
        }

        if (contractVersion.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Message contract version cannot contain whitespace.", nameof(contractVersion));
        }

        return contractVersion;
    }

    /// <summary>
    /// Validates message-name format and constraints.
    /// </summary>
    private static void _ValidateMessageNameFormat(string messageName)
    {
        const int maxMessageNameLength = 255;

        if (messageName.Length > maxMessageNameLength)
        {
            throw new ArgumentException(
                $"Message name '{messageName}' exceeds maximum length of {maxMessageNameLength} characters.",
                nameof(messageName)
            );
        }

        if (messageName.StartsWith('.') || messageName.EndsWith('.'))
        {
            throw new ArgumentException(
                $"Message name '{messageName}' cannot start or end with a dot.",
                nameof(messageName)
            );
        }

        if (messageName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Message name '{messageName}' cannot contain consecutive dots.",
                nameof(messageName)
            );
        }

        foreach (var c in messageName)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_')
            {
                throw new ArgumentException(
                    $"Message name '{messageName}' contains invalid character '{c}'. Only alphanumeric characters, dots, hyphens, and underscores are allowed.",
                    nameof(messageName)
                );
            }
        }
    }
}
