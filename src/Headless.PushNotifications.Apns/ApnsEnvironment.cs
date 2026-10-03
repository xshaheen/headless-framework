// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using FluentValidation;
using FluentValidation.Results;
using Headless.PushNotifications.Apns.Internal;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// The APNs environment a provider delivers to.
/// </summary>
[PublicAPI]
public enum ApnsEnvironment
{
    /// <summary>The production environment, <c>api.push.apple.com</c>, for App Store, TestFlight, and ad hoc builds.</summary>
    Production = 0,

    /// <summary>The development environment, <c>api.sandbox.push.apple.com</c>, for builds signed with a development profile.</summary>
    Sandbox = 1,
}
