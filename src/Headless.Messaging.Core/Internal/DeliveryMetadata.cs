// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

[StructLayout(LayoutKind.Auto)]
internal readonly record struct DeliveryMetadataValues(
    DeliveryMode? RequestedDeliveryMode,
    DeliveryMode? ResolvedDeliveryMode,
    bool? IsCoordinated = null
);

internal static class DeliveryMetadata
{
    private const string _True = "true";
    private const string _False = "false";

    internal static void Stamp(IDictionary<string, string?> headers, in DeliveryDecision decision)
    {
        headers[Headers.RequestedDeliveryMode] = decision.RequestedMode.ToString("G");
        headers[Headers.ResolvedDeliveryMode] = decision.ResolvedMode.ToString("G");
        headers[Headers.DeliveryCoordinated] = decision.IsTransactional ? _True : _False;
    }

    internal static DeliveryMetadataValues Read(IDictionary<string, string?> headers)
    {
        var hasRequested = headers.TryGetValue(Headers.RequestedDeliveryMode, out var requestedValue);
        var hasResolved = headers.TryGetValue(Headers.ResolvedDeliveryMode, out var resolvedValue);
        var hasCoordinated = headers.TryGetValue(Headers.DeliveryCoordinated, out var coordinatedValue);

        if (!hasRequested && !hasResolved && !hasCoordinated)
        {
            return default;
        }

        return new(_Parse(requestedValue), _Parse(resolvedValue), _ParseCoordinated(coordinatedValue));
    }

    internal static DeliveryMetadataValues ReadStoredHeaders(IDictionary<string, string?> headers)
    {
        var hasMetadata =
            headers.ContainsKey(Headers.RequestedDeliveryMode)
            || headers.ContainsKey(Headers.ResolvedDeliveryMode)
            || headers.ContainsKey(Headers.DeliveryCoordinated);

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

    // Same exact-value rule as the delivery modes: an absent, unknown, or customer-controlled value projects as
    // "not recorded". A row that predates this header must read as unknown, never as false — reporting it as
    // "not coordinated" asserts an answer the row never carried.
    private static bool? _ParseCoordinated(string? value)
    {
        if (string.Equals(value, _True, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(value, _False, StringComparison.Ordinal) ? false : null;
    }
}
