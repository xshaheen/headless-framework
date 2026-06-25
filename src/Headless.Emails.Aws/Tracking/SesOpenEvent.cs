// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about an <c>Open</c> event.</summary>
[PublicAPI]
public sealed record SesOpenEvent
{
    /// <summary>The date and time when the open event occurred.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The recipient's IP address.</summary>
    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; init; } = null!;

    /// <summary>The user agent of the device or email client the recipient used to open the email.</summary>
    [JsonPropertyName("userAgent")]
    public string UserAgent { get; init; } = null!;
}
