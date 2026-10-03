// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Controls how runtime subscription conflicts are handled.
/// </summary>
[PublicAPI]
public enum RuntimeSubscriptionDuplicateBehavior
{
    /// <summary>
    /// Reject duplicate registrations with an exception.
    /// </summary>
    Reject = 0,

    /// <summary>
    /// Ignore duplicate registrations and keep the existing subscription attached.
    /// </summary>
    Ignore = 1,

    /// <summary>
    /// Replace the existing duplicate registration with the new subscription.
    /// </summary>
    Replace = 2,
}
