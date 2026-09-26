// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Net;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// Maps an APNs HTTP status and <c>reason</c> onto an <see cref="ApnsFailureKind"/>, following the retry guidance
/// in Apple's "Handling notification responses from APNs".
/// </summary>
internal static class ApnsFailureClassifier
{
    /// <summary>
    /// The reasons whose classification is not a plain status-class default. Every reason in Apple's response error
    /// string table appears exactly once; an unknown reason falls back to its status class.
    /// </summary>
    private static readonly FrozenDictionary<string, ApnsFailureKind> _ReasonKinds = _BuildReasonKinds();

    private static FrozenDictionary<string, ApnsFailureKind> _BuildReasonKinds()
    {
        var kinds = new Dictionary<string, ApnsFailureKind>(StringComparer.Ordinal)
        {
            // 400 Bad request. Apple: "Most notifications with the status code 4XX can be retried after you fix the
            // error noted in the reason field" — so a 4xx the caller fixes is a payload/configuration problem, not
            // an unchanged-request retry.
            ["BadCollapseId"] = ApnsFailureKind.Payload,
            // "The specified device token is invalid. Verify that the request contains a valid token and that the
            // token matches the environment." Also named in Apple's never-retry list.
            ["BadDeviceToken"] = ApnsFailureKind.DeviceTokenInvalid,
            ["BadExpirationDate"] = ApnsFailureKind.Payload,
            ["BadMessageId"] = ApnsFailureKind.Payload,
            ["BadPriority"] = ApnsFailureKind.Payload,
            ["BadTopic"] = ApnsFailureKind.Configuration,
            // "The device token doesn't match the specified topic." Apple names it in the never-retry list, and the
            // topic comes from the instance's configuration.
            ["DeviceTokenNotForTopic"] = ApnsFailureKind.Configuration,
            ["DuplicateHeaders"] = ApnsFailureKind.Payload,
            // "Idle timeout." The connection idled out, not the request: the same push can go out again.
            ["IdleTimeout"] = ApnsFailureKind.Transport,
            ["InvalidPushType"] = ApnsFailureKind.Payload,
            ["MissingDeviceToken"] = ApnsFailureKind.Payload,
            // "The apns-topic header of the request isn't specified and is required."
            ["MissingTopic"] = ApnsFailureKind.Configuration,
            ["PayloadEmpty"] = ApnsFailureKind.Payload,
            // "Pushing to this topic is not allowed."
            ["TopicDisallowed"] = ApnsFailureKind.Configuration,
            // 403 "There was an error with the certificate or with the provider's authentication token."
            ["BadCertificate"] = ApnsFailureKind.Configuration,
            // "The client certificate doesn't match the environment."
            ["BadCertificateEnvironment"] = ApnsFailureKind.Configuration,
            // "The provider token is stale and a new token should be generated." The service already re-minted and
            // retried once before a result reaches the caller, so a result carrying it stayed stale.
            ["ExpiredProviderToken"] = ApnsFailureKind.Authentication,
            // "The specified action is not allowed."
            ["Forbidden"] = ApnsFailureKind.Configuration,
            // "The provider token is not valid, or the token signature can't be verified."
            ["InvalidProviderToken"] = ApnsFailureKind.Authentication,
            // "No provider certificate was used to connect to APNs, and the authorization header is missing or no
            // provider token is specified."
            ["MissingProviderToken"] = ApnsFailureKind.Authentication,
            // "The key ID in the provider token isn't related to the key ID of the token used in the first push of
            // this connection."
            ["UnrelatedKeyIdInToken"] = ApnsFailureKind.Authentication,
            // "The key ID in the provider token doesn't match the environment."
            ["BadEnvironmentKeyIdInToken"] = ApnsFailureKind.Authentication,
            // 404 "The request contained an invalid :path value." and 405 "The request used an invalid :method
            // value." — the request's own shape, which only this provider builds.
            ["BadPath"] = ApnsFailureKind.Payload,
            ["MethodNotAllowed"] = ApnsFailureKind.Payload,
            // 410 "The device token is no longer active for the topic." Apple names both reasons in its
            // never-retry list.
            ["ExpiredToken"] = ApnsFailureKind.DeviceTokenInvalid,
            ["Unregistered"] = ApnsFailureKind.DeviceTokenInvalid,
            // 413 "The message payload is too large." Apple names it in the never-retry list.
            ["PayloadTooLarge"] = ApnsFailureKind.Payload,
            // 429 "Too many requests were made consecutively to the same device token." — throttling.
            ["TooManyRequests"] = ApnsFailureKind.Throttled,
            // 429 "The provider's authentication token is being updated too often. Update the authentication token
            // no more than once every 20 minutes." — a token-lifecycle problem, not device throttling.
            ["TooManyProviderTokenUpdates"] = ApnsFailureKind.Authentication,
            // 500/503 are left to the status class: both are ServerError.
        };

        return kinds.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// The configuration-level authentication problems that get their own error event: the key or token identity is
    /// wrong, so no send succeeds until the operator fixes the configuration.
    /// </summary>
    public static bool IsTokenConfigurationProblem(string? reason)
    {
        return reason is "InvalidProviderToken" or "MissingProviderToken" or "UnrelatedKeyIdInToken";
    }

    /// <summary>
    /// Classifies a rejection by its reason first and its status class second, so a reason Apple adds later still
    /// lands in a usable kind.
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
