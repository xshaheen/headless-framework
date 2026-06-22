// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>Shared keys for the FCM resilience pipeline, referenced by both registration and the sender.</summary>
internal static class FcmResilienceKeys
{
    /// <summary>Name of the FCM retry resilience pipeline registered in DI.</summary>
    public const string RetryPipeline = "Headless:FcmRetry";
}
