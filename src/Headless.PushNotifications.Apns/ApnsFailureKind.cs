// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Specifies the failure classification of an APNs send operation.
/// </summary>
[PublicAPI]
public enum ApnsFailureKind
{
    /// <summary>
    /// The device token is invalid, expired, unregistered, or assigned to a different topic or environment.
    /// </summary>
    DeviceTokenInvalid = 0,

    /// <summary>
    /// APNs throttled the request with status code 429.
    /// </summary>
    Throttled = 1,

    /// <summary>
    /// APNs returned a server error with status code 5xx.
    /// </summary>
    ServerError = 2,

    /// <summary>
    /// APNs rejected the provider authentication token or credentials.
    /// </summary>
    Authentication = 3,

    /// <summary>
    /// The APNs configuration is invalid, including incorrect topic, certificate, or environment settings.
    /// </summary>
    Configuration = 4,

    /// <summary>
    /// The notification request or payload is malformed, missing required headers, or exceeds the size limit.
    /// </summary>
    Payload = 5,

    /// <summary>
    /// A transport, network, timeout, or connection fault prevented communication with APNs.
    /// </summary>
    Transport = 6,
}
