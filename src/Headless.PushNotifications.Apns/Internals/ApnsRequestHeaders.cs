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
    /// The collapse id is blank or exceeds 64 UTF-8 bytes, the priority is not allowed for the push type, or the
    /// instance is configured for VoIP and the push type is not an alert.
    /// </exception>
    public static ApnsRequestHeaders Create(ApnsNotification notification, ApnsOptions options)
    {
        Argument.IsNotNull(notification);
        Argument.IsNotNull(options);

        var (pushType, topic, priority) = notification switch
        {
            ApnsAlertNotification alert when options.PushType == ApnsPushType.Voip => (
                "voip",
                $"{options.BundleId}.voip",
                alert.Priority ?? options.Priority
            ),
            ApnsAlertNotification alert => ("alert", options.BundleId, alert.Priority ?? options.Priority),
            ApnsVoipDataNotification voipData when options.PushType == ApnsPushType.Voip => (
                "voip",
                $"{options.BundleId}.voip",
                voipData.Priority ?? options.Priority
            ),
            ApnsBackgroundNotification => _NotVoip(
                options,
                "background",
                options.BundleId,
                ApnsPriority.PowerConsiderate
            ),
            ApnsLiveActivityNotification liveActivity => _NotVoip(
                options,
                "liveactivity",
                $"{options.BundleId}.push-type.liveactivity",
                _LiveActivityPriority(liveActivity.Priority)
            ),
            ApnsLocationNotification location => _NotVoip(
                options,
                "location",
                $"{options.BundleId}.location-query",
                _NichePriority(location.Priority)
            ),
            ApnsPushToTalkNotification => _NotVoip(
                options,
                "pushtotalk",
                $"{options.BundleId}.voip-ptt",
                ApnsPriority.Immediate
            ),
            ApnsWidgetsNotification widgets => _NotVoip(
                options,
                "widgets",
                $"{options.BundleId}.push-type.widgets",
                _NichePriority(widgets.Priority)
            ),
            ApnsControlsNotification controls => _NotVoip(
                options,
                "controls",
                $"{options.BundleId}.push-type.controls",
                _NichePriority(controls.Priority)
            ),
            ApnsComplicationNotification complication => _NotVoip(
                options,
                "complication",
                $"{options.BundleId}.complication",
                _NichePriority(complication.Priority)
            ),
            ApnsFileProviderNotification fileProvider => _NotVoip(
                options,
                "fileprovider",
                $"{options.BundleId}.pushkit.fileprovider",
                _NichePriority(fileProvider.Priority)
            ),
            _ => throw new ArgumentException(
                $"Unsupported APNs notification type '{notification.GetType().Name}'.",
                nameof(notification)
            ),
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

        // Apple advises against delivering a stale push-to-talk push, so it defaults to deliver-once.
        var requested =
            notification.Expiration ?? (notification is ApnsPushToTalkNotification ? ApnsExpiration.DeliverOnce : null);

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
