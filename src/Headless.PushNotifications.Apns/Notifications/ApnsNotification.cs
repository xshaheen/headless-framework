// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    /// <see langword="null"/>, which sends no header so APNs applies its own storage policy.
    /// </summary>
    public ApnsExpiration? Expiration { get; init; }

    /// <summary>
    /// The identifier that merges notifications into one on the device, sent as <c>apns-collapse-id</c>. At most 64
    /// UTF-8 bytes.
    /// </summary>
    public string? CollapseId { get; init; }
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

    /// <summary>Custom keys written beside <c>aps</c> at the payload's top level. The key <c>aps</c> is reserved.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

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
    /// <summary>Custom keys written beside <c>aps</c> at the payload's top level. The key <c>aps</c> is reserved.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }
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
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.PowerConsiderate"/>. Apple budgets <see cref="ApnsPriority.Immediate"/> Live Activity
    /// pushes per hour and does not allow <see cref="ApnsPriority.PowerPrioritized"/>.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}
