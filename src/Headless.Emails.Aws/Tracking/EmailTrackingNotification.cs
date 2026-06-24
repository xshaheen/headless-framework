// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>
/// A single email sending event record published by Amazon SES to an event destination
/// (for example an Amazon SNS topic) for a configuration set. The <see cref="EventType"/>
/// field determines which of the event-specific properties is populated.
/// </summary>
/// <remarks>
/// Deserialize the SES message body into this type, then switch on <see cref="EventType"/>
/// (see <see cref="EmailEventTypes"/>) to read the matching event object.
/// </remarks>
[PublicAPI]
public sealed record EmailTrackingNotification
{
    /// <summary>
    /// A string that describes the type of event. One of the <see cref="EmailEventTypes"/> values:
    /// <c>Delivery</c>, <c>Send</c>, <c>Reject</c>, <c>Open</c>, <c>Click</c>, <c>Bounce</c>,
    /// <c>Complaint</c>, <c>Rendering Failure</c>, or <c>DeliveryDelay</c>.
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = null!;

    /// <summary>An object that contains information about the email that produced the event.</summary>
    [JsonPropertyName("mail")]
    public MailDetails Mail { get; init; } = null!;

    /// <summary>Present only when <see cref="EventType"/> is <c>Send</c>. Always empty.</summary>
    [JsonPropertyName("send")]
    public SendEvent? Send { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>Delivery</c>.</summary>
    [JsonPropertyName("delivery")]
    public DeliveryEvent? Delivery { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>DeliveryDelay</c>.</summary>
    [JsonPropertyName("deliveryDelay")]
    public DeliveryDelayEvent? DeliveryDelay { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>Open</c>.</summary>
    [JsonPropertyName("open")]
    public OpenEvent? Open { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>Click</c>.</summary>
    [JsonPropertyName("click")]
    public ClickEvent? Click { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>Bounce</c>.</summary>
    [JsonPropertyName("bounce")]
    public BounceEvent? Bounce { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>Complaint</c>.</summary>
    [JsonPropertyName("complaint")]
    public ComplaintEvent? Complaint { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>Reject</c>.</summary>
    [JsonPropertyName("reject")]
    public RejectEvent? Reject { get; init; }

    /// <summary>Present only when <see cref="EventType"/> is <c>Rendering Failure</c>.</summary>
    [JsonPropertyName("failure")]
    public RenderingFailureEvent? Failure { get; init; }

    /// <summary>Captures any fields SES adds to the record that are not modeled above.</summary>
    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
