// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>Shared keys for the FCM resilience pipeline, referenced by both registration and the sender.</summary>
internal static class FcmResilienceKeys
{
    private const string _RetryPipelinePrefix = "Headless:FcmRetry";

    /// <summary>
    /// Name of the FCM retry resilience pipeline registered in DI. The default (unkeyed) sender uses the bare
    /// prefix; each named instance gets its own pipeline suffixed with the instance name, so per-instance retry
    /// options never bleed across instances.
    /// </summary>
    public static string GetRetryPipelineKey(string? name)
    {
        return name is null ? _RetryPipelinePrefix : $"{_RetryPipelinePrefix}:{name}";
    }
}
