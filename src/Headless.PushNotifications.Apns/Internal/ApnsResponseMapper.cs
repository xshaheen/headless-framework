// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Maps APNs HTTP responses to <see cref="ApnsSendResult"/> and <see cref="PushNotificationResponse"/> models.
/// </summary>
internal static class ApnsResponseMapper
{
    public const string ExpiredProviderTokenReason = "ExpiredProviderToken";

    private const string _BadDeviceTokenReason = "BadDeviceToken";

    private static readonly TimeSpan _ServerRetryAfter = TimeSpan.FromMinutes(15);

    private static readonly ApnsErrorBody _NoError = new(Reason: null, Timestamp: null);

    private static readonly long _MinUnixMilliseconds = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
    private static readonly long _MaxUnixMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    /// <summary>
    /// Deserializes the error reason and timestamp from an APNs error response payload.
    /// </summary>
    public static ApnsErrorBody ReadError(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return _NoError;
        }

        try
        {
            var error = JsonSerializer.Deserialize(body, ApnsJsonSerializerContext.Default.ApnsErrorBody);

            return error is null
                ? _NoError
                : error with
                {
                    Reason = string.IsNullOrWhiteSpace(error.Reason) ? null : error.Reason,
                };
        }
        catch (JsonException)
        {
            // Non-JSON responses from proxies or gateways default to no error details.
            return _NoError;
        }
    }

    public static ApnsSendResult CreateResult(
        string deviceToken,
        string apnsId,
        HttpStatusCode status,
        ApnsErrorBody error,
        string? uniqueId,
        bool treatBadDeviceTokenAsUnregistered
    )
    {
        var failureKind = status == HttpStatusCode.OK ? null : ApnsFailureClassifier.Classify(status, error.Reason);

        return new ApnsSendResult
        {
            Response = Map(deviceToken, apnsId, status, error.Reason, treatBadDeviceTokenAsUnregistered),
            StatusCode = status,
            Reason = error.Reason,
            ApnsId = apnsId,
            UniqueId = string.IsNullOrWhiteSpace(uniqueId) ? null : uniqueId,
            InvalidSince = status == HttpStatusCode.Gone ? _FromUnixMilliseconds(error.Timestamp) : null,
            FailureKind = failureKind,
            RetryAfter = failureKind switch
            {
                ApnsFailureKind.ServerError => _ServerRetryAfter,
                ApnsFailureKind.Throttled when error.RetryAfter is { } retryAfter => retryAfter,
                _ => null,
            },
        };
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

        if (status == HttpStatusCode.Gone)
        {
            return PushNotificationResponse.Unregistered(deviceToken);
        }

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

    private static DateTimeOffset? _FromUnixMilliseconds(long? milliseconds)
    {
        return milliseconds is { } value && value >= _MinUnixMilliseconds && value <= _MaxUnixMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : null;
    }
}
