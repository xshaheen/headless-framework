// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Headless.PushNotifications.Apns;

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
