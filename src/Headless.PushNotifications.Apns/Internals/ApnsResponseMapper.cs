// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Maps an APNs HTTP outcome onto an <see cref="ApnsSendResult"/> and its provider-agnostic
/// <see cref="PushNotificationResponse"/>.
/// </summary>
internal static class ApnsResponseMapper
{
    public const string ExpiredProviderTokenReason = "ExpiredProviderToken";

    private const string _BadDeviceTokenReason = "BadDeviceToken";

    // Apple: "After 15 minutes, you can retry JSON payloads that receive response status codes that begin with 5XX."
    private static readonly TimeSpan _ServerRetryAfter = TimeSpan.FromMinutes(15);

    private static readonly ApnsErrorBody _NoError = new(Reason: null, Timestamp: null);

    private static readonly long _MinUnixMilliseconds = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
    private static readonly long _MaxUnixMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    /// <summary>
    /// Reads the <c>reason</c> and, for HTTP 410, the millisecond <c>timestamp</c> from an APNs error body; each is
    /// <see langword="null"/> when the body does not carry it.
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
            // A proxy or load balancer can answer with HTML or plain text; the HTTP status still describes the
            // outcome, so the missing reason is not an error of its own.
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
                // A 429 waits only as long as APNs says: Apple's response-header table does not list Retry-After,
                // so a missing header leaves the delay to the caller.
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

    private static DateTimeOffset? _FromUnixMilliseconds(long? milliseconds)
    {
        // A value outside DateTimeOffset's range would throw and turn a clear 410 into a failure, so it reads as
        // "no timestamp" instead.
        return milliseconds is { } value && value >= _MinUnixMilliseconds && value <= _MaxUnixMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(value)
            : null;
    }
}
