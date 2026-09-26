// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>A validated notification ready to send: its JSON payload and its APNs request headers.</summary>
internal sealed record ApnsPreparedNotification(byte[] Payload, ApnsRequestHeaders Headers);

/// <summary>The APNs request headers a notification decides, computed from its type and the instance options.</summary>
/// <param name="PushType">The <c>apns-push-type</c> value.</param>
/// <param name="Topic">The <c>apns-topic</c> value: the bundle identifier plus the push type's suffix.</param>
/// <param name="Priority">The <c>apns-priority</c> value.</param>
/// <param name="Expiration">The <c>apns-expiration</c> value in Unix epoch seconds, or <see langword="null"/> to omit it.</param>
/// <param name="CollapseId">The <c>apns-collapse-id</c> value, or <see langword="null"/> to omit it.</param>
internal sealed record ApnsRequestHeaders(
    string PushType,
    string Topic,
    ApnsPriority Priority,
    long? Expiration,
    string? CollapseId
)
{
    // Apple caps the apns-collapse-id header at 64 bytes.
    private const int _MaxCollapseIdBytes = 64;

    /// <summary>Computes the headers for <paramref name="notification"/> sent through an instance with <paramref name="options"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The collapse id is blank or exceeds 64 UTF-8 bytes, the priority is not allowed for the push type, the
    /// instance is configured for VoIP and the push type is not an alert or VoIP push, or a VoIP push is sent through
    /// an instance that is not configured for VoIP.
    /// </exception>
    public static ApnsRequestHeaders Create(ApnsNotification notification, ApnsOptions options)
    {
        Argument.IsNotNull(notification);
        Argument.IsNotNull(options);

        // Each typed notification maps onto its push type, so a raw notification of the same type follows the same
        // topic, priority, and VoIP rules without a second copy of them.
        var (type, requestedPriority) = notification switch
        {
            ApnsAlertNotification alert => (ApnsNotificationType.Alert, alert.Priority),
            ApnsVoipDataNotification voipData => (ApnsNotificationType.Voip, voipData.Priority),
            ApnsBackgroundNotification => (ApnsNotificationType.Background, null),
            ApnsLiveActivityNotification liveActivity => (ApnsNotificationType.LiveActivity, liveActivity.Priority),
            ApnsLocationNotification location => (ApnsNotificationType.Location, location.Priority),
            ApnsPushToTalkNotification => (ApnsNotificationType.PushToTalk, null),
            ApnsWidgetsNotification widgets => (ApnsNotificationType.Widgets, widgets.Priority),
            ApnsControlsNotification controls => (ApnsNotificationType.Controls, controls.Priority),
            ApnsComplicationNotification complication => (ApnsNotificationType.Complication, complication.Priority),
            ApnsFileProviderNotification fileProvider => (ApnsNotificationType.FileProvider, fileProvider.Priority),
            ApnsRawNotification raw => (raw.Type, raw.Priority),
            _ => throw new ArgumentException(
                $"Unsupported APNs notification type '{notification.GetType().Name}'.",
                nameof(notification)
            ),
        };

        Argument.IsInEnum(type, paramName: nameof(notification));

        var (pushType, topic, priority) = type switch
        {
            ApnsNotificationType.Alert or ApnsNotificationType.Voip when options.PushType == ApnsPushType.Voip => (
                ApnsPushTypes.Voip,
                $"{options.BundleId}.voip",
                requestedPriority ?? options.Priority
            ),
            ApnsNotificationType.Alert => (
                ApnsPushTypes.Alert,
                options.BundleId,
                requestedPriority ?? options.Priority
            ),
            ApnsNotificationType.Voip => throw new ArgumentException(
                "A VoIP push needs an APNs instance configured for VoIP, whose PushKit tokens receive it.",
                nameof(notification)
            ),
            ApnsNotificationType.Background => _NotVoip(
                options,
                ApnsPushTypes.Background,
                options.BundleId,
                _FixedPriority(requestedPriority, ApnsPriority.PowerConsiderate, ApnsPushTypes.Background)
            ),
            ApnsNotificationType.LiveActivity => _NotVoip(
                options,
                ApnsPushTypes.LiveActivity,
                $"{options.BundleId}.push-type.liveactivity",
                _LiveActivityPriority(requestedPriority)
            ),
            ApnsNotificationType.Location => _NotVoip(
                options,
                ApnsPushTypes.Location,
                $"{options.BundleId}.location-query",
                _NichePriority(requestedPriority)
            ),
            ApnsNotificationType.PushToTalk => _NotVoip(
                options,
                ApnsPushTypes.PushToTalk,
                $"{options.BundleId}.voip-ptt",
                _FixedPriority(requestedPriority, ApnsPriority.Immediate, ApnsPushTypes.PushToTalk)
            ),
            ApnsNotificationType.Widgets => _NotVoip(
                options,
                ApnsPushTypes.Widgets,
                $"{options.BundleId}.push-type.widgets",
                _NichePriority(requestedPriority)
            ),
            ApnsNotificationType.Controls => _NotVoip(
                options,
                ApnsPushTypes.Controls,
                $"{options.BundleId}.push-type.controls",
                _NichePriority(requestedPriority)
            ),
            ApnsNotificationType.Complication => _NotVoip(
                options,
                ApnsPushTypes.Complication,
                $"{options.BundleId}.complication",
                _NichePriority(requestedPriority)
            ),
            ApnsNotificationType.FileProvider => _NotVoip(
                options,
                ApnsPushTypes.FileProvider,
                $"{options.BundleId}.pushkit.fileprovider",
                _NichePriority(requestedPriority)
            ),
            _ => throw new ArgumentException($"Unsupported APNs notification type '{type}'.", nameof(notification)),
        };

        Argument.IsInEnum(priority, paramName: nameof(notification));

        if (notification.CollapseId is not null)
        {
            Argument.IsNotNullOrWhiteSpace(notification.CollapseId, paramName: nameof(notification));
            Argument.IsLessThanOrEqualTo(
                Encoding.UTF8.GetByteCount(notification.CollapseId),
                _MaxCollapseIdBytes,
                "The APNs collapse id exceeds 64 UTF-8 bytes."
            );
        }

        // Apple tells senders to use expiration 0 for push-to-talk and VoIP pushes: a call or speaker update that
        // APNs stores and delivers later is worse than none. Keyed on the push type, so an alert sent through a VoIP
        // instance gets the default too.
        var requested =
            notification.Expiration
            ?? (pushType is ApnsPushTypes.PushToTalk or ApnsPushTypes.Voip ? ApnsExpiration.DeliverOnce : null);

        var expiration = requested switch
        {
            null => (long?)null,
            { ExpiresAt: { } expiresAt } => expiresAt.ToUnixTimeSeconds(),
            _ => 0L,
        };

        return new ApnsRequestHeaders(pushType, topic, priority, expiration, notification.CollapseId);
    }

    /// <summary>Returns the headers as name and value pairs in wire order, omitting the unset optional headers.</summary>
    public IEnumerable<KeyValuePair<string, string>> Enumerate()
    {
        yield return new("apns-push-type", PushType);
        yield return new("apns-topic", Topic);
        yield return new("apns-priority", ((int)Priority).ToString(CultureInfo.InvariantCulture));

        if (Expiration is { } expiration)
        {
            yield return new("apns-expiration", expiration.ToString(CultureInfo.InvariantCulture));
        }

        if (CollapseId is not null)
        {
            yield return new("apns-collapse-id", CollapseId);
        }
    }

    private static (string PushType, string Topic, ApnsPriority Priority) _NotVoip(
        ApnsOptions options,
        string pushType,
        string topic,
        ApnsPriority priority
    )
    {
        // A VoIP instance holds PushKit tokens, which APNs rejects for every push type but voip.
        if (options.PushType == ApnsPushType.Voip)
        {
            throw new ArgumentException(
                $"An APNs instance configured for VoIP cannot send a '{pushType}' push; PushKit tokens receive only VoIP pushes.",
                nameof(options)
            );
        }

        return (pushType, topic, priority);
    }

    private static ApnsPriority _FixedPriority(ApnsPriority? requested, ApnsPriority required, string pushType)
    {
        // Apple fixes the priority of background (5) and push-to-talk (10) pushes; only a raw notification can ask
        // for another, and it is refused rather than silently corrected.
        if (requested is { } priority && priority != required)
        {
            throw new ArgumentException(
                $"An APNs '{pushType}' push must use priority {((int)required).ToString(CultureInfo.InvariantCulture)}.",
                nameof(requested)
            );
        }

        return required;
    }

    private static ApnsPriority _NichePriority(ApnsPriority? priority)
    {
        // These push types are not power-managed alerts, so priority 1 is refused; 10 matches APNs's own default when
        // the header is absent.
        if (priority == ApnsPriority.PowerPrioritized)
        {
            throw new ArgumentException(
                "This APNs push type cannot use priority 1 (PowerPrioritized); use 5 or 10.",
                nameof(priority)
            );
        }

        return priority ?? ApnsPriority.Immediate;
    }

    private static ApnsPriority _LiveActivityPriority(ApnsPriority? priority)
    {
        // Priority 5 by default because Apple budgets priority-10 Live Activity pushes per hour; priority 1 is not
        // allowed for this push type at all.
        if (priority == ApnsPriority.PowerPrioritized)
        {
            throw new ArgumentException(
                "A Live Activity push cannot use priority 1 (PowerPrioritized); use 5 or 10.",
                nameof(priority)
            );
        }

        return priority ?? ApnsPriority.PowerConsiderate;
    }
}
