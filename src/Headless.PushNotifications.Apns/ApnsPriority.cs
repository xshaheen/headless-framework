// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;
using Headless.PushNotifications.Apns.Internal;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The APNs delivery priority. The numeric values are the <c>apns-priority</c> header values.
/// </summary>
#pragma warning disable CA1008 // The values are the apns-priority header values, and APNs defines no zero priority.
[PublicAPI]
public enum ApnsPriority
{
    /// <summary>Prioritize the device's power over all other factors; the notification may be delayed.</summary>
    PowerPrioritized = 1,

    /// <summary>Deliver based on the device's power considerations.</summary>
    PowerConsiderate = 5,

    /// <summary>Deliver immediately.</summary>
    Immediate = 10,
}
