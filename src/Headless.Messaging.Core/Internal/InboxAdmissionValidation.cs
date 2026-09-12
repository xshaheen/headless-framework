// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Internal;

internal static class InboxAdmissionValidation
{
    public static string? Validate(
        string name,
        string consumerIdentity,
        string contractVersion,
        MediumMessage message,
        long generation
    )
    {
        _ValidateInboxIdentity(name, 200, nameof(name));
        _ValidateInboxIdentity(consumerIdentity, ConsumerMetadata.ConsumerIdentityMaxLength, nameof(consumerIdentity));
        _ValidateInboxIdentity(contractVersion, 100, nameof(contractVersion));
        _ValidateInboxIdentity(message.Origin.Id, MessageOptions.MessageIdMaxLength, "message.Origin.Id");
        if (generation < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                generation,
                "Inbox generation cannot be negative."
            );
        }

        _ = MessageLaneCompatibility.ToPersistedValue(message.Lane);
        if (
            !message.Origin.Headers.TryGetValue(Headers.TenantId, out var rawTenant)
            || string.IsNullOrWhiteSpace(rawTenant)
        )
        {
            return null;
        }

        if (rawTenant.Length > MessageOptions.TenantIdMaxLength)
        {
            throw new ArgumentException(
                $"Inbox tenant identity must be {MessageOptions.TenantIdMaxLength} characters or fewer.",
                nameof(message)
            );
        }

        return TenantContextScope.ResolveTenantId(message.Origin.Headers, logger: null);
    }

    private static void _ValidateInboxIdentity(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Inbox identity values cannot be null or whitespace.", parameterName);
        }

        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Inbox identity value must be {maximumLength} characters or fewer."
                ),
                parameterName
            );
        }
    }
}
