// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;
using Headless.PushNotifications.Apns.Internal;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The APNs push type, which decides how the device handles the notification.
/// </summary>
[PublicAPI]
public enum ApnsPushType
{
    /// <summary>A user-visible notification with an alert.</summary>
    Alert = 0,

    /// <summary>A VoIP notification delivered to PushKit. It raises the payload limit to 5120 bytes.</summary>
    Voip = 1,
}
