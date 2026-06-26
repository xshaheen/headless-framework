// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>Information about a <c>Reject</c> event.</summary>
[PublicAPI]
public sealed record SesRejectEvent
{
    /// <summary>
    /// The reason the email was rejected. The only possible value is <c>Bad content</c>, which means SES
    /// detected that the email contained a virus. When a message is rejected SES stops processing it and
    /// does not attempt delivery.
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = null!;
}
