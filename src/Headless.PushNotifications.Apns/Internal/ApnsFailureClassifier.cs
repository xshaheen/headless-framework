// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>
/// Maps APNs HTTP response status codes and reason strings to <see cref="ApnsFailureKind"/> values.
/// </summary>
internal static class ApnsFailureClassifier
{
    private static readonly FrozenDictionary<string, ApnsFailureKind> _ReasonKinds = _BuildReasonKinds();

    private static FrozenDictionary<string, ApnsFailureKind> _BuildReasonKinds()
    {
        var kinds = new Dictionary<string, ApnsFailureKind>(StringComparer.Ordinal)
        {
            ["BadCollapseId"] = ApnsFailureKind.Payload,
            ["BadDeviceToken"] = ApnsFailureKind.DeviceTokenInvalid,
            ["BadExpirationDate"] = ApnsFailureKind.Payload,
            ["BadMessageId"] = ApnsFailureKind.Payload,
            ["BadPriority"] = ApnsFailureKind.Payload,
            ["BadTopic"] = ApnsFailureKind.Configuration,
            ["DeviceTokenNotForTopic"] = ApnsFailureKind.Configuration,
            ["DuplicateHeaders"] = ApnsFailureKind.Payload,
            ["IdleTimeout"] = ApnsFailureKind.Transport,
            ["InvalidPushType"] = ApnsFailureKind.Payload,
            ["MissingDeviceToken"] = ApnsFailureKind.Payload,
            ["MissingTopic"] = ApnsFailureKind.Configuration,
            ["PayloadEmpty"] = ApnsFailureKind.Payload,
            ["TopicDisallowed"] = ApnsFailureKind.Configuration,
            ["BadCertificate"] = ApnsFailureKind.Configuration,
            ["BadCertificateEnvironment"] = ApnsFailureKind.Configuration,
            ["ExpiredProviderToken"] = ApnsFailureKind.Authentication,
            ["Forbidden"] = ApnsFailureKind.Configuration,
            ["InvalidProviderToken"] = ApnsFailureKind.Authentication,
            ["MissingProviderToken"] = ApnsFailureKind.Authentication,
            ["UnrelatedKeyIdInToken"] = ApnsFailureKind.Authentication,
            ["BadEnvironmentKeyIdInToken"] = ApnsFailureKind.Authentication,
            ["BadPath"] = ApnsFailureKind.Payload,
            ["MethodNotAllowed"] = ApnsFailureKind.Payload,
            ["ExpiredToken"] = ApnsFailureKind.DeviceTokenInvalid,
            ["Unregistered"] = ApnsFailureKind.DeviceTokenInvalid,
            ["PayloadTooLarge"] = ApnsFailureKind.Payload,
            ["TooManyRequests"] = ApnsFailureKind.Throttled,
            ["TooManyProviderTokenUpdates"] = ApnsFailureKind.Authentication,
        };

        return kinds.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Determines whether the reason represents an authentication configuration defect requiring administrative correction.
    /// </summary>
    public static bool IsTokenConfigurationProblem(string? reason)
    {
        return reason is "InvalidProviderToken" or "MissingProviderToken" or "UnrelatedKeyIdInToken";
    }

    /// <summary>
    /// Classifies an APNs delivery failure by reason code and HTTP status code.
    /// </summary>
    public static ApnsFailureKind? Classify(HttpStatusCode? status, string? reason)
    {
        if (status is null)
        {
            // No answer arrived: transport fault, open circuit, timeout, or a refused endpoint.
            return ApnsFailureKind.Transport;
        }

        if (reason is not null && _ReasonKinds.TryGetValue(reason, out var kind))
        {
            return kind;
        }

        return _ClassifyByStatus(status.Value);
    }

    private static ApnsFailureKind _ClassifyByStatus(HttpStatusCode status)
    {
        var code = (int)status;

        if (code >= 500)
        {
            return ApnsFailureKind.ServerError;
        }

        return status switch
        {
            HttpStatusCode.Gone => ApnsFailureKind.DeviceTokenInvalid,
            HttpStatusCode.TooManyRequests => ApnsFailureKind.Throttled,
            // Apple describes 403 as "There was an error with the certificate or with the provider's
            // authentication token", so an unknown 403 reason is most likely a credential problem.
            HttpStatusCode.Forbidden => ApnsFailureKind.Authentication,
            // 4xx "can be retried after you fix the error noted in the reason field", so the request itself is the
            // problem; the fix is a new request, not a retry.
            _ => ApnsFailureKind.Payload,
        };
    }
}
