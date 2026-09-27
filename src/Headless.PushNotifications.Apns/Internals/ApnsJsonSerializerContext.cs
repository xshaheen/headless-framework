// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>The JSON body APNs returns with a rejected request.</summary>
/// <param name="Reason">The APNs error code, such as <c>BadDeviceToken</c>.</param>
/// <param name="Timestamp">
/// For HTTP 410, the milliseconds since the Unix epoch at which APNs confirmed the token was no longer valid.
/// </param>
internal sealed record ApnsErrorBody(
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("timestamp")] long? Timestamp
)
{
    // Not serialized: filled from the Retry-After response header, which a 429 may carry.
    public TimeSpan? RetryAfter { get; init; }
}

// Source-generated so reading the error body stays trim- and AOT-safe.
[JsonSerializable(typeof(ApnsErrorBody))]
internal sealed partial class ApnsJsonSerializerContext : JsonSerializerContext;
