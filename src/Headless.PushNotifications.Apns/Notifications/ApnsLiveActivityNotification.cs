// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

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
    /// The broadcast channel the started activity subscribes to, written as <c>aps.input-push-channel</c>. The
    /// activity then receives the updates sent with
    /// <see cref="IApnsPushNotificationService.SendBroadcastAsync"/> on that channel. Start only; iOS 18 and iPadOS
    /// 18 or later.
    /// </summary>
    public string? InputPushChannel { get; init; }

    /// <summary>
    /// The delivery priority, sent as <c>apns-priority</c>. Default: <see langword="null"/>, which sends
    /// <see cref="ApnsPriority.PowerConsiderate"/>. Apple budgets <see cref="ApnsPriority.Immediate"/> Live Activity
    /// pushes per hour and does not allow <see cref="ApnsPriority.PowerPrioritized"/>.
    /// </summary>
    public ApnsPriority? Priority { get; init; }
}
