// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.PushNotifications.Apns;

/// <summary>
/// An APNs notification. Each sealed subtype is one APNs push type and fixes that type's <c>apns-push-type</c>
/// header, topic, priority rules, and payload fields.
/// </summary>
/// <remarks>
/// The hierarchy is closed to this assembly, so a notification's push type is decided by its type rather than
/// checked at send time: an alert on a background push cannot be expressed.
/// </remarks>
[PublicAPI]
public abstract record ApnsNotification
{
    // private protected rather than private: a private constructor is unreachable from top-level subtypes.
    private protected ApnsNotification() { }

    /// <summary>
    /// When APNs stops trying to deliver the notification, sent as <c>apns-expiration</c>. Default:
    /// <see langword="null"/>, which sends no header so APNs applies its own storage policy, except on VoIP and
    /// push-to-talk pushes, where it sends <see cref="ApnsExpiration.DeliverOnce"/> as Apple instructs.
    /// </summary>
    public ApnsExpiration? Expiration { get; init; }

    /// <summary>
    /// The identifier that merges notifications into one on the device, sent as <c>apns-collapse-id</c>. At most 64
    /// UTF-8 bytes.
    /// </summary>
    public string? CollapseId { get; init; }

    /// <summary>
    /// The identifier sent as <c>apns-id</c> and returned as <see cref="ApnsSendResult.ApnsId"/>. Default:
    /// <see langword="null"/>, which sends a new random UUID. Set it to correlate the send with your own records; a
    /// resend after an expired provider token keeps it.
    /// </summary>
    /// <remarks>Only a single-token send accepts it: a multicast refuses it, because every request needs its own id.</remarks>
    public Guid? ApnsId { get; init; }
}

/// <summary>
/// A user-visible notification: an alert, a badge, a sound, or any mix of them. Sent with push type <c>alert</c>,
/// or <c>voip</c> through an instance configured with <see cref="ApnsPushType.Voip"/>.
/// </summary>
/// <remarks>At least one of <see cref="Alert"/>, <see cref="Badge"/>, or <see cref="Sound"/> must be set.</remarks>
[PublicAPI]
public sealed record ApnsAlertNotification : ApnsNotification
{
    /// <summary>The visible content, written as <c>aps.alert</c>.</summary>
    public ApnsAlert? Alert { get; init; }

    /// <summary>The app icon badge count, written as <c>aps.badge</c>. <c>0</c> clears the badge.</summary>
    public int? Badge { get; init; }

    /// <summary>The sound to play, written as <c>aps.sound</c>.</summary>
    public ApnsSound? Sound { get; init; }

    /// <summary>The identifier that groups related notifications, written as <c>aps.thread-id</c>.</summary>
    public string? ThreadId { get; init; }

    /// <summary>The app-registered notification category that supplies actions, written as <c>aps.category</c>.</summary>
    public string? Category { get; init; }

    /// <summary>
    /// Whether the app's notification service extension may modify the notification before display, written as
    /// <c>aps.mutable-content: 1</c>.
    /// </summary>
    public bool MutableContent { get; init; }

    /// <summary>How urgently the system presents the notification, written as <c>aps.interruption-level</c>.</summary>
    public ApnsInterruptionLevel? InterruptionLevel { get; init; }

    /// <summary>
    /// How the system ranks this notification in the notification summary, from 0 to 1, written as
    /// <c>aps.relevance-score</c>.
    /// </summary>
    public double? RelevanceScore { get; init; }

    /// <summary>The identifier of the app window to bring forward, written as <c>aps.target-content-id</c>.</summary>
    public string? TargetContentId { get; init; }

    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which uses
    /// <see cref="ApnsOptions.Priority"/>.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}

/// <summary>
/// A silent push that wakes the app in the background, written as <c>aps.content-available: 1</c> plus custom data.
/// Sent with push type <c>background</c> at priority 5, which Apple requires for this push type.
/// </summary>
/// <remarks>
/// The system throttles background pushes and may drop them, so do not rely on one arriving. A VoIP instance
/// refuses it, because PushKit tokens cannot receive background pushes.
/// </remarks>
[PublicAPI]
public sealed record ApnsBackgroundNotification : ApnsNotification
{
    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }
}

/// <summary>
/// A push that starts, updates, or ends a Live Activity. Sent with push type <c>liveactivity</c> to the
/// <c>&lt;bundle&gt;.push-type.liveactivity</c> topic.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ContentState"/> and <see cref="Attributes"/> are copied into the payload as is. Serialize them with
/// default JSON naming and date strategies: ActivityKit decodes them with its defaults, so a custom naming or date
/// strategy makes the device fail to decode the state.
/// </para>
/// <para>
/// <see cref="JsonElement"/> has no value equality, so two notifications with the same content compare unequal.
/// </para>
/// <para>
/// A <see cref="ApnsLiveActivityEvent.Start"/> needs <see cref="ContentState"/>, <see cref="AttributesType"/>,
/// <see cref="Attributes"/>, and <see cref="Alert"/>. An <see cref="ApnsLiveActivityEvent.Update"/> needs
/// <see cref="ContentState"/>. An <see cref="ApnsLiveActivityEvent.End"/> may omit it, though Apple advises sending
/// the final state so the ended activity shows the latest data. A VoIP instance refuses this push type.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record ApnsLiveActivityNotification : ApnsNotification
{
    /// <summary>The lifecycle event, written as <c>aps.event</c>.</summary>
    public required ApnsLiveActivityEvent Event { get; init; }

    /// <summary>
    /// When the content changed, written as <c>aps.timestamp</c> in Unix epoch seconds. The device ignores an update
    /// older than the one it shows. Default: <see langword="null"/>, which writes the clock's current time.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// The activity's dynamic content, a JSON object matching the app's <c>ContentState</c> type, written as
    /// <c>aps.content-state</c>.
    /// </summary>
    public JsonElement? ContentState { get; init; }

    /// <summary>When the system marks the activity's content as outdated, written as <c>aps.stale-date</c>.</summary>
    public DateTimeOffset? StaleDate { get; init; }

    /// <summary>
    /// When the system removes an ended activity from the Lock Screen, written as <c>aps.dismissal-date</c>.
    /// </summary>
    public DateTimeOffset? DismissalDate { get; init; }

    /// <summary>
    /// How the system ranks this activity against the app's other activities, written as
    /// <c>aps.relevance-score</c>. A higher value ranks higher.
    /// </summary>
    public double? RelevanceScore { get; init; }

    /// <summary>
    /// The alert that lights the screen for this change, written as <c>aps.alert</c>. Only the title and the body
    /// apply. Required on <see cref="ApnsLiveActivityEvent.Start"/>.
    /// </summary>
    public ApnsAlert? Alert { get; init; }

    /// <summary>
    /// The sound the <see cref="Alert"/> plays, written inside it as <c>aps.alert.sound</c>. Needs
    /// <see cref="Alert"/>, and takes a named sound only, not a critical one.
    /// </summary>
    public ApnsSound? Sound { get; init; }

    /// <summary>
    /// The name of the app's <c>ActivityAttributes</c> type, written as <c>aps.attributes-type</c>. Start only.
    /// </summary>
    public string? AttributesType { get; init; }

    /// <summary>
    /// The activity's static attributes, a JSON object matching <see cref="AttributesType"/>, written as
    /// <c>aps.attributes</c>. Start only.
    /// </summary>
    public JsonElement? Attributes { get; init; }

    /// <summary>
    /// Whether the started activity reports a push token for its later updates, written as
    /// <c>aps.input-push-token: 1</c>. Start only; iOS 18 and iPadOS 18 or later.
    /// </summary>
    public bool RequestPushToken { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.PowerConsiderate"/>. Apple budgets <see cref="ApnsPriority.Immediate"/> Live Activity
    /// pushes per hour and does not allow <see cref="ApnsPriority.PowerPrioritized"/>.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}

/// <summary>
/// A push that asks the app's Location Push Service Extension for the device's location. Sent with push type
/// <c>location</c> to the <c>&lt;bundle&gt;.location-query</c> topic, written as an empty <c>aps</c> dictionary plus
/// custom data.
/// </summary>
/// <remarks>
/// Apple documents no payload for this push type, so the extension receives only the custom data. A VoIP instance
/// refuses this push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsLocationNotification : ApnsNotification
{
    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}

/// <summary>
/// A push that tells the app's PushToTalk channel about new audio or a change of speaker. Sent with push type
/// <c>pushtotalk</c> to the <c>&lt;bundle&gt;.voip-ptt</c> topic at priority 10, written as an empty <c>aps</c>
/// dictionary plus custom data.
/// </summary>
/// <remarks>
/// A stale push-to-talk push is worse than none, so a <see langword="null"/> <see cref="ApnsNotification.Expiration"/>
/// sends <see cref="ApnsExpiration.DeliverOnce"/> rather than letting APNs store it; set an explicit expiration to
/// override. The token is the one the PushToTalk framework reports, not a PushKit VoIP token, so a VoIP instance
/// refuses this push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsPushToTalkNotification : ApnsNotification
{
    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }
}

/// <summary>
/// A push that tells WidgetKit the app's widgets have new content to reload. Sent with push type <c>widgets</c> to
/// the <c>&lt;bundle&gt;.push-type.widgets</c> topic, always written as <c>{"aps":{"content-changed":true}}</c>.
/// </summary>
/// <remarks>
/// WidgetKit also serves the watch complications that replace ClockKit, so this push type reloads them too. A VoIP
/// instance refuses this push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsWidgetsNotification : ApnsNotification
{
    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}

/// <summary>
/// A push that tells the system the app's controls have new state to reload. Sent with push type <c>controls</c> to
/// the <c>&lt;bundle&gt;.push-type.controls</c> topic, always written as <c>{"aps":{"content-changed":true}}</c>.
/// </summary>
/// <remarks>A VoIP instance refuses this push type.</remarks>
[PublicAPI]
public sealed record ApnsControlsNotification : ApnsNotification
{
    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}

/// <summary>
/// A push that updates a ClockKit watch complication. Sent with push type <c>complication</c> to the
/// <c>&lt;bundle&gt;.complication</c> topic, written as an empty <c>aps</c> dictionary plus custom data.
/// </summary>
/// <remarks>
/// WidgetKit supersedes ClockKit, and <see cref="ApnsWidgetsNotification"/> reloads widget-based complications.
/// Apple's reference renders this topic's suffix as <c>h.complication</c>, which reads as a typo for
/// <c>.complication</c>; a <c>TopicDisallowed</c> rejection points at that discrepancy. A VoIP instance refuses this
/// push type.
/// </remarks>
[PublicAPI]
public sealed record ApnsComplicationNotification : ApnsNotification
{
    /// <summary>
    /// Custom keys written beside <c>aps</c> at the payload's top level, each with any JSON value. The key
    /// <c>aps</c> is reserved. Serialized once per send and never modified.
    /// </summary>
    public JsonObject? Data { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}

/// <summary>
/// A push that signals a File Provider domain to sync its changes. Sent with push type <c>fileprovider</c> to the
/// <c>&lt;bundle&gt;.pushkit.fileprovider</c> topic, written as the top-level <c>container-identifier</c> and
/// <c>domain</c> keys with no <c>aps</c> dictionary.
/// </summary>
/// <remarks>A VoIP instance refuses this push type.</remarks>
[PublicAPI]
public sealed record ApnsFileProviderNotification : ApnsNotification
{
    /// <summary>
    /// The item container that changed, written as <c>container-identifier</c>. Must not be blank.
    /// </summary>
    public required string ContainerIdentifier { get; init; }

    /// <summary>The File Provider domain identifier, written as <c>domain</c>. Must not be blank.</summary>
    public required string Domain { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.Immediate"/>. <see cref="ApnsPriority.PowerPrioritized"/> is not allowed.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}

/// <summary>
/// A notification whose payload is supplied as JSON and sent verbatim: the escape hatch for Apple payload keys the
/// typed notifications do not model. <see cref="Type"/> decides the push type and with it every rule a typed
/// notification of that type follows: the topic, the priority rules, the payload size limit, and whether a VoIP or
/// certificate-authenticated instance can send it.
/// </summary>
/// <remarks>
/// <para>
/// The payload must be a JSON object whose bytes read as strict JSON (no trailing commas or comments). Its keys are
/// not validated, so custom keys placed inside <c>aps</c> are sent as given even though APNs ignores them. <see cref="JsonElement"/> has no value equality, so two raw notifications with the
/// same payload compare unequal.
/// </para>
/// <para>
/// <see cref="ApnsNotificationType.Voip"/> needs an instance configured with <see cref="ApnsPushType.Voip"/>, which
/// also sends <see cref="ApnsNotificationType.Alert"/> as a VoIP push, as it does for
/// <see cref="ApnsAlertNotification"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record ApnsRawNotification : ApnsNotification
{
    /// <summary>The push type, which decides the <c>apns-push-type</c> header, the topic, and the push type's rules.</summary>
    public required ApnsNotificationType Type { get; init; }

    /// <summary>The complete payload, a JSON object written as is and counted against the push type's size limit.</summary>
    public required JsonElement Payload { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which uses the push
    /// type's default. A priority the push type does not allow is refused, as it is for the typed notification.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}
