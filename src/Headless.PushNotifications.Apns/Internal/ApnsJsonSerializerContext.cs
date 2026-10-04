// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns.Internal;

// Source-generated context for AOT-compatible JSON serialization.
[JsonSerializable(typeof(ApnsErrorBody))]
internal sealed partial class ApnsJsonSerializerContext : JsonSerializerContext;

/// <summary>Represents the JSON error response body returned by APNs on rejection.</summary>
/// <param name="Reason">The APNs error reason code.</param>
/// <param name="Timestamp">The Unix epoch millisecond timestamp indicating when the device token expired.</param>
internal sealed record ApnsErrorBody(
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("timestamp")] long? Timestamp
)
{
    // Populated from the Retry-After response header when present on 429 responses.
    public TimeSpan? RetryAfter { get; init; }
}
