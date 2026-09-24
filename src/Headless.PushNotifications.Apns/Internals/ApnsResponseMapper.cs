// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>Maps an APNs HTTP outcome onto the provider-agnostic <see cref="PushNotificationResponse"/>.</summary>
internal static class ApnsResponseMapper
{
    public const string ExpiredProviderTokenReason = "ExpiredProviderToken";

    private const string _BadDeviceTokenReason = "BadDeviceToken";

    /// <summary>Reads the <c>reason</c> from an APNs error body, or <see langword="null"/> when there is none.</summary>
    public static string? ReadReason(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        try
        {
            var error = JsonSerializer.Deserialize(body, ApnsJsonSerializerContext.Default.ApnsErrorBody);

            return string.IsNullOrWhiteSpace(error?.Reason) ? null : error.Reason;
        }
        catch (JsonException)
        {
            // A proxy or load balancer can answer with HTML or plain text; the HTTP status still describes the
            // outcome, so the missing reason is not an error of its own.
            return null;
        }
    }

    public static PushNotificationResponse Map(
        string deviceToken,
        string apnsId,
        HttpStatusCode status,
        string? reason,
        bool treatBadDeviceTokenAsUnregistered
    )
    {
        if (status == HttpStatusCode.OK)
        {
            return PushNotificationResponse.Succeeded(deviceToken, apnsId);
        }

        // Apple defines 410 as a token that is no longer active for the topic, whatever reason accompanies it.
        if (status == HttpStatusCode.Gone)
        {
            return PushNotificationResponse.Unregistered(deviceToken);
        }

        // BadDeviceToken also means "token from the other environment", so reporting it as unregistered is opt-in:
        // a host pointed at the wrong environment would otherwise discard every valid token it holds.
        if (
            treatBadDeviceTokenAsUnregistered
            && status == HttpStatusCode.BadRequest
            && string.Equals(reason, _BadDeviceTokenReason, StringComparison.Ordinal)
        )
        {
            return PushNotificationResponse.Unregistered(deviceToken);
        }

        return PushNotificationResponse.Failed(deviceToken, DescribeRejection(status, reason));
    }

    public static string DescribeRejection(HttpStatusCode status, string? reason)
    {
        var code = ((int)status).ToString(CultureInfo.InvariantCulture);

        return reason is null ? $"APNs rejected the request (HTTP {code})" : $"{reason} (HTTP {code})";
    }

    public static string DescribeException(Exception exception)
    {
        return $"{exception.GetType().Name}: {exception.Message}";
    }
}
