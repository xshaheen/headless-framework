// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

internal readonly record struct DeliveryMetadataValues(
    DeliveryMode? RequestedDeliveryMode,
    DeliveryMode? ResolvedDeliveryMode
);

internal static class DeliveryMetadata
{
    internal static void Stamp(IDictionary<string, string?> headers, in DeliveryDecision decision)
    {
        headers[Headers.RequestedDeliveryMode] = decision.RequestedMode.ToString("G");
        headers[Headers.ResolvedDeliveryMode] = decision.ResolvedMode.ToString("G");
    }

    internal static DeliveryMetadataValues Read(IDictionary<string, string?> headers)
    {
        var hasRequested = headers.TryGetValue(Headers.RequestedDeliveryMode, out var requestedValue);
        var hasResolved = headers.TryGetValue(Headers.ResolvedDeliveryMode, out var resolvedValue);

        if (!hasRequested && !hasResolved)
        {
            return default;
        }

        return new(_Parse(requestedValue), _Parse(resolvedValue));
    }

    internal static DeliveryMetadataValues ReadStoredHeaders(IDictionary<string, string?> headers)
    {
        var hasMetadata =
            headers.ContainsKey(Headers.RequestedDeliveryMode) || headers.ContainsKey(Headers.ResolvedDeliveryMode);

        return hasMetadata ? Read(headers) : new(null, DeliveryMode.Durable);
    }

    internal static DeliveryMetadataValues ReadStoredEnvelope(ISerializer serializer, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            var envelope = serializer.Deserialize(content);
            return envelope is null ? default : ReadStoredHeaders(envelope.Headers);
        }
        catch (Exception)
        {
            // Monitoring is best-effort: one unreadable legacy/corrupt envelope must not fail the containing page.
            return default;
        }
    }

    private static DeliveryMode? _Parse(string? value)
    {
        if (
            value is not null
            && Enum.TryParse<DeliveryMode>(value, ignoreCase: false, out var mode)
            && Enum.IsDefined(mode)
        )
        {
            return mode;
        }

        return null;
    }
}
