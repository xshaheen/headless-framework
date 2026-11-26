// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications;

/// <summary>
/// A push notification that a development service captured instead of delivering, as read from
/// <see cref="IPushNotificationCaptureStore"/>. A multicast send is captured once, with every client identifier in
/// <see cref="ClientIdentifiers"/>.
/// </summary>
/// <remarks>
/// The capture is a snapshot: the client identifiers and data are copied when the send runs, so a caller that changes
/// its request afterwards does not change what the store shows.
/// </remarks>
[PublicAPI]
public sealed record CapturedPushNotification
{
    /// <summary>
    /// Gets the payload size, in bytes, above which Firebase Cloud Messaging and APNs reject a notification: 4096.
    /// </summary>
    public const int MaxPayloadBytes = 4096;

    /// <summary>Gets the capture identifier, unique within the process. Order captures by <see cref="Sequence"/>, not by this value.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the store <see cref="IPushNotificationCaptureStore.Version"/> at which this notification was captured. Sequences increase strictly in capture order, so this is the ordering key.
    /// Pass it as <see cref="PushNotificationCaptureQuery.AfterSequence"/> to read only the notifications captured
    /// after it.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>Gets the time the send was captured, read from the registered <see cref="TimeProvider"/>. Never earlier than the capture before it.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    /// Gets the name of the service instance that captured the notification: the <c>AddNamed</c> name, or an empty
    /// string for the default (unnamed) service.
    /// </summary>
    public required string SenderName { get; init; }

    /// <summary>Gets the target device identifiers: one for a single-device send, every identifier of a multicast.</summary>
    public required IReadOnlyList<string> ClientIdentifiers { get; init; }

    /// <summary>Gets a value indicating whether the notification came through a multicast send.</summary>
    public bool IsMulticast { get; init; }

    /// <summary>Gets the title, or <see langword="null"/> for a data-only message.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the body, or <see langword="null"/> for a data-only message.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// Gets a value indicating whether the request was a data-only message, which wakes the application and shows
    /// nothing.
    /// </summary>
    public bool IsDataOnly { get; init; }

    /// <summary>Gets a copy of the data payload, or <see langword="null"/> when the request had none.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>Gets the request's <see cref="PushNotificationRequest.CollapseKey"/>.</summary>
    public string? CollapseKey { get; init; }

    /// <summary>Gets the request's <see cref="PushNotificationRequest.Badge"/>.</summary>
    public int? Badge { get; init; }

    /// <summary>Gets the request's <see cref="PushNotificationRequest.Sound"/>.</summary>
    public string? Sound { get; init; }

    /// <summary>Gets the request's <see cref="PushNotificationRequest.Priority"/>.</summary>
    public PushNotificationPriority? Priority { get; init; }

    /// <summary>Gets the request's <see cref="PushNotificationRequest.TimeToLive"/>.</summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>
    /// Gets the UTF-8 size of the notification's JSON payload (title, body, sound, badge, and data). It approximates
    /// what a provider sends; each provider adds its own envelope.
    /// </summary>
    public int PayloadSizeBytes { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="PayloadSizeBytes"/> is over <see cref="MaxPayloadBytes"/>, so a real
    /// provider would likely reject the notification. The development service still accepts it.
    /// </summary>
    public bool ExceedsPayloadLimit => PayloadSizeBytes > MaxPayloadBytes;

    /// <summary>
    /// Gets why the send failed, for example a malformed request, or <see langword="null"/> when the service accepted it.
    /// </summary>
    public string? FailureError { get; init; }

    /// <summary>Gets a value indicating whether the service accepted the notification.</summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Succeeded => FailureError is null;

    /// <summary>
    /// Gets the W3C trace id of the <see cref="System.Diagnostics.Activity"/> that was current during the send, or
    /// <see langword="null"/> when none was.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Gets the correlation id that the registered <c>ICorrelationIdProvider</c> reported during the send, or
    /// <see langword="null"/> when none is registered or active.
    /// </summary>
    public string? CorrelationId { get; init; }
}
