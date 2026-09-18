// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Messaging.Serialization;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct DeliveryMetadataValues(
    DeliveryMode? RequestedDeliveryMode,
    DeliveryMode? ResolvedDeliveryMode,
    TransactionEnlistment? RequestedEnlistment = null
);

internal static class DeliveryMetadata
{
    internal static void Stamp(IDictionary<string, string?> headers, in DeliveryDecision decision)
    {
        headers[Headers.RequestedDeliveryMode] = decision.RequestedMode.ToString("G");
        headers[Headers.ResolvedDeliveryMode] = decision.ResolvedMode.ToString("G");
        headers[Headers.RequestedEnlistment] = decision.Enlistment.ToString("G");
    }

    internal static DeliveryMetadataValues Read(IDictionary<string, string?> headers)
    {
        var hasRequested = headers.TryGetValue(Headers.RequestedDeliveryMode, out var requestedValue);
        var hasResolved = headers.TryGetValue(Headers.ResolvedDeliveryMode, out var resolvedValue);
        var hasEnlistment = headers.TryGetValue(Headers.RequestedEnlistment, out var enlistmentValue);

        if (!hasRequested && !hasResolved && !hasEnlistment)
        {
            return default;
        }

        return new(_Parse(requestedValue), _Parse(resolvedValue), _ParseEnlistment(enlistmentValue));
    }

    internal static DeliveryMetadataValues ReadStoredHeaders(IDictionary<string, string?> headers)
    {
        var hasMetadata =
            headers.ContainsKey(Headers.RequestedDeliveryMode)
            || headers.ContainsKey(Headers.ResolvedDeliveryMode)
            || headers.ContainsKey(Headers.RequestedEnlistment);

        return hasMetadata
            ? Read(headers)
            : new(RequestedDeliveryMode: null, ResolvedDeliveryMode: DeliveryMode.Durable);
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
#pragma warning disable ERP022 // Monitoring must treat unreadable legacy/corrupt envelopes as missing metadata.
        catch (Exception)
        {
            // Monitoring is best-effort: one unreadable legacy/corrupt envelope must not fail the containing page.
            return default;
        }
#pragma warning restore ERP022
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

    // Same exact-name rule as the delivery modes: an unknown or customer-controlled value projects as "not recorded".
    private static TransactionEnlistment? _ParseEnlistment(string? value)
    {
        if (
            value is not null
            && Enum.TryParse<TransactionEnlistment>(value, ignoreCase: false, out var enlistment)
            && Enum.IsDefined(enlistment)
        )
        {
            return enlistment;
        }

        return null;
    }
}
