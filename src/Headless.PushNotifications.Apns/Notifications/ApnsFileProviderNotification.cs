// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

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
