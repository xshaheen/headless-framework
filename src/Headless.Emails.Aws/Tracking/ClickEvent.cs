// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about a <c>Click</c> event.</summary>
[PublicAPI]
public sealed record ClickEvent
{
    /// <summary>The date and time when the click event occurred.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The recipient's IP address.</summary>
    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; init; } = null!;

    /// <summary>The user agent of the client the recipient used to click a link in the email.</summary>
    [JsonPropertyName("userAgent")]
    public string UserAgent { get; init; } = null!;

    /// <summary>The URL of the link that the recipient clicked.</summary>
    [JsonPropertyName("link")]
    public string Link { get; init; } = null!;

    /// <summary>
    /// The tags that were added to the link using the <c>ses:tags</c> attribute. SES sends this as a JSON
    /// object where each key maps to a list of values.
    /// </summary>
    [JsonPropertyName("linkTags")]
    public IReadOnlyDictionary<string, string[]>? LinkTags { get; init; }
}
