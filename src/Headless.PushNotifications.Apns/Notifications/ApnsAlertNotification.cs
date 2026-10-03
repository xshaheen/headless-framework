// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

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
