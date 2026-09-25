// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>Validates a notification and writes it as the APNs JSON payload.</summary>
/// <remarks>
/// Written by hand with <see cref="Utf8JsonWriter"/> rather than a serializer so the output stays AOT-safe and its
/// exact byte length is known for the payload-size check.
/// </remarks>
internal static class ApnsPayloadWriter
{
    // Apple's documented payload limits: 4 KB for regular notifications and 5 KB for VoIP.
    private const int _MaxAlertPayloadBytes = 4096;
    private const int _MaxVoipPayloadBytes = 5120;

    // The payload's own dictionary, where Apple reads alert, badge, and sound; a custom key with this name would
    // overwrite it.
    private const string _ApsKey = "aps";

    private const string _VoipPushType = "voip";

    /// <summary>
    /// Validates <paramref name="notification"/> for an instance configured with <paramref name="options"/> and
    /// returns its UTF-8 JSON payload with the request headers it decides.
    /// </summary>
    /// <param name="notification">The notification to send.</param>
    /// <param name="options">The sending instance's options, which supply the bundle id, push type, and priority.</param>
    /// <param name="timeProvider">The clock that stamps a Live Activity push with no explicit timestamp.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The notification breaks a rule of its push type, a data key is <c>aps</c>, the collapse id exceeds 64 UTF-8
    /// bytes, the payload exceeds the push type's size limit, or the instance cannot send the push type.
    /// </exception>
    public static ApnsPreparedNotification Prepare(
        ApnsNotification notification,
        ApnsOptions options,
        TimeProvider timeProvider
    )
    {
        Argument.IsNotNull(notification);
        Argument.IsNotNull(options);
        Argument.IsNotNull(timeProvider);

        // Headers first: they refuse a push type the instance cannot send before any payload work.
        var headers = ApnsRequestHeaders.Create(notification, options);
        var buffer = new ArrayBufferWriter<byte>(512);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            switch (notification)
            {
                case ApnsAlertNotification alert:
                    _WriteAlertNotification(writer, alert);
                    break;
                case ApnsBackgroundNotification background:
                    _WriteBackgroundNotification(writer, background);
                    break;
                case ApnsLiveActivityNotification liveActivity:
                    _WriteLiveActivityNotification(writer, liveActivity, timeProvider);
                    break;
                case ApnsVoipDataNotification voipData:
                    _WriteVoipDataNotification(writer, voipData);
                    break;
                default:
                    throw new ArgumentException(
                        $"Unsupported APNs notification type '{notification.GetType().Name}'.",
                        nameof(notification)
                    );
            }
        }

        var limit = string.Equals(headers.PushType, _VoipPushType, StringComparison.Ordinal)
            ? _MaxVoipPayloadBytes
            : _MaxAlertPayloadBytes;

        _EnsureWithinLimit(buffer.WrittenCount, limit, headers.PushType, nameof(notification));

        return new ApnsPreparedNotification(buffer.WrittenSpan.ToArray(), headers);
    }

    #region Alert

    private static void _WriteAlertNotification(Utf8JsonWriter writer, ApnsAlertNotification notification)
    {
        // An alert push with nothing to present is a silent push sent with the wrong push type; APNs may not
        // deliver it.
        if (notification.Alert is null && notification.Badge is null && notification.Sound is null)
        {
            throw new ArgumentException(
                "An APNs alert notification needs an alert, a badge, or a sound. Use ApnsBackgroundNotification for a silent push.",
                nameof(notification)
            );
        }

        if (notification.Badge is { } badge)
        {
            Argument.IsPositiveOrZero(badge, paramName: nameof(notification));
        }

        if (notification.RelevanceScore is { } score)
        {
            _EnsureRelevanceScore(score, nameof(notification));
        }

        if (notification.Alert is not null)
        {
            _ValidateAlert(notification.Alert, liveActivity: false);
        }

        _EnsureNoReservedKey(notification.Data, nameof(notification));

        writer.WriteStartObject();
        writer.WriteStartObject(_ApsKey);

        if (notification.Alert is not null)
        {
            _WriteAlert(writer, notification.Alert);
        }

        if (notification.Badge is { } badgeValue)
        {
            writer.WriteNumber("badge", badgeValue);
        }

        if (notification.Sound is not null)
        {
            _WriteSound(writer, notification.Sound);
        }

        _WriteOptionalString(writer, "thread-id", notification.ThreadId);
        _WriteOptionalString(writer, "category", notification.Category);

        if (notification.MutableContent)
        {
            writer.WriteNumber("mutable-content", 1);
        }

        if (notification.InterruptionLevel is { } level)
        {
            writer.WriteString("interruption-level", _InterruptionLevelValue(level));
        }

        if (notification.RelevanceScore is { } relevance)
        {
            writer.WriteNumber("relevance-score", relevance);
        }

        _WriteOptionalString(writer, "target-content-id", notification.TargetContentId);

        writer.WriteEndObject();
        _WriteData(writer, notification.Data);
        writer.WriteEndObject();
    }

    private static void _WriteAlert(Utf8JsonWriter writer, ApnsAlert alert)
    {
        writer.WriteStartObject("alert");
        _WriteOptionalString(writer, "title", alert.Title);
        _WriteOptionalString(writer, "subtitle", alert.Subtitle);
        _WriteOptionalString(writer, "body", alert.Body);
        _WriteOptionalString(writer, "title-loc-key", alert.TitleLocKey);
        _WriteOptionalArray(writer, "title-loc-args", alert.TitleLocArgs);
        _WriteOptionalString(writer, "subtitle-loc-key", alert.SubtitleLocKey);
        _WriteOptionalArray(writer, "subtitle-loc-args", alert.SubtitleLocArgs);
        _WriteOptionalString(writer, "loc-key", alert.LocKey);
        _WriteOptionalArray(writer, "loc-args", alert.LocArgs);
        _WriteOptionalString(writer, "launch-image", alert.LaunchImage);
        writer.WriteEndObject();
    }

    private static void _WriteSound(Utf8JsonWriter writer, ApnsSound sound)
    {
        if (!sound.IsCritical)
        {
            writer.WriteString("sound", sound.Name);

            return;
        }

        writer.WriteStartObject("sound");
        writer.WriteNumber("critical", 1);
        writer.WriteString("name", sound.Name);

        if (sound.Volume is { } volume)
        {
            writer.WriteNumber("volume", volume);
        }

        writer.WriteEndObject();
    }

    private static string _InterruptionLevelValue(ApnsInterruptionLevel level)
    {
        return level switch
        {
            ApnsInterruptionLevel.Passive => "passive",
            ApnsInterruptionLevel.Active => "active",
            ApnsInterruptionLevel.TimeSensitive => "time-sensitive",
            ApnsInterruptionLevel.Critical => "critical",
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown APNs interruption level."),
        };
    }

    #endregion

    #region Background

    private static void _WriteBackgroundNotification(Utf8JsonWriter writer, ApnsBackgroundNotification notification)
    {
        _EnsureNoReservedKey(notification.Data, nameof(notification));

        writer.WriteStartObject();
        writer.WriteStartObject(_ApsKey);
        writer.WriteNumber("content-available", 1);
        writer.WriteEndObject();
        _WriteData(writer, notification.Data);
        writer.WriteEndObject();
    }

    #endregion

    #region VoIP data

    private static void _WriteVoipDataNotification(Utf8JsonWriter writer, ApnsVoipDataNotification notification)
    {
        _EnsureNoReservedKey(notification.Data, nameof(notification));

        // PushKit hands the whole payload to the app, but APNs still expects the aps dictionary to be present.
        writer.WriteStartObject();
        writer.WriteStartObject(_ApsKey);
        writer.WriteEndObject();
        _WriteData(writer, notification.Data);
        writer.WriteEndObject();
    }

    #endregion

    #region Live Activity

    private static void _WriteLiveActivityNotification(
        Utf8JsonWriter writer,
        ApnsLiveActivityNotification notification,
        TimeProvider timeProvider
    )
    {
        _ValidateLiveActivity(notification);

        var timestamp = notification.Timestamp ?? timeProvider.GetUtcNow();

        writer.WriteStartObject();
        writer.WriteStartObject(_ApsKey);
        writer.WriteNumber("timestamp", timestamp.ToUnixTimeSeconds());
        writer.WriteString("event", _LiveActivityEventValue(notification.Event));

        if (notification.ContentState is { } contentState)
        {
            writer.WritePropertyName("content-state");
            contentState.WriteTo(writer);
        }

        if (notification.StaleDate is { } staleDate)
        {
            writer.WriteNumber("stale-date", staleDate.ToUnixTimeSeconds());
        }

        if (notification.DismissalDate is { } dismissalDate)
        {
            writer.WriteNumber("dismissal-date", dismissalDate.ToUnixTimeSeconds());
        }

        if (notification.RelevanceScore is { } relevance)
        {
            writer.WriteNumber("relevance-score", relevance);
        }

        _WriteOptionalString(writer, "attributes-type", notification.AttributesType);

        if (notification.Attributes is { } attributes)
        {
            writer.WritePropertyName("attributes");
            attributes.WriteTo(writer);
        }

        if (notification.Alert is not null)
        {
            _WriteLiveActivityAlert(writer, notification.Alert, notification.Sound);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void _ValidateLiveActivity(ApnsLiveActivityNotification notification)
    {
        Argument.IsInEnum(notification.Event, paramName: nameof(notification));

        if (notification.ContentState is { } contentState)
        {
            _EnsureJsonObject(contentState, "content state");
        }
        else if (notification.Event != ApnsLiveActivityEvent.End)
        {
            throw new ArgumentException(
                $"A Live Activity '{_LiveActivityEventValue(notification.Event)}' push needs a content state.",
                nameof(notification)
            );
        }

        if (notification.Event == ApnsLiveActivityEvent.Start)
        {
            // ActivityKit creates the activity from these, so a start without them cannot be displayed.
            if (string.IsNullOrWhiteSpace(notification.AttributesType) || notification.Attributes is null)
            {
                throw new ArgumentException(
                    "A Live Activity 'start' push needs an attributes type and attributes.",
                    nameof(notification)
                );
            }

            if (notification.Alert is null)
            {
                throw new ArgumentException("A Live Activity 'start' push needs an alert.", nameof(notification));
            }
        }
        else if (notification.AttributesType is not null || notification.Attributes is not null)
        {
            throw new ArgumentException(
                "Only a Live Activity 'start' push carries an attributes type and attributes.",
                nameof(notification)
            );
        }

        if (notification.Attributes is { } attributes)
        {
            _EnsureJsonObject(attributes, "attributes");
        }

        if (notification.RelevanceScore is { } score && !double.IsFinite(score))
        {
            throw new ArgumentException(
                "A Live Activity relevance score must be a finite number.",
                nameof(notification)
            );
        }

        if (notification.Alert is not null)
        {
            _ValidateAlert(notification.Alert, liveActivity: true);
        }

        if (notification.Sound is not null)
        {
            // The sound plays with the alert, so it has nowhere to go without one; Live Activity alerts take only a
            // sound name.
            if (notification.Alert is null)
            {
                throw new ArgumentException("A Live Activity sound needs an alert to play with.", nameof(notification));
            }

            if (notification.Sound.IsCritical)
            {
                throw new ArgumentException(
                    "A Live Activity alert plays only a named sound, not a critical sound.",
                    nameof(notification)
                );
            }
        }
    }

    private static void _WriteLiveActivityAlert(Utf8JsonWriter writer, ApnsAlert alert, ApnsSound? sound)
    {
        // A Live Activity alert writes each text as a literal string or as a {loc-key, loc-args} dictionary, not the
        // flat *-loc-key keys of an ordinary alert, and carries its sound inside the alert rather than beside it.
        writer.WriteStartObject("alert");
        _WriteLiveActivityText(writer, "title", alert.Title, alert.TitleLocKey, alert.TitleLocArgs);
        _WriteLiveActivityText(writer, "body", alert.Body, alert.LocKey, alert.LocArgs);
        _WriteOptionalString(writer, "sound", sound?.Name);
        writer.WriteEndObject();
    }

    private static void _WriteLiveActivityText(
        Utf8JsonWriter writer,
        string propertyName,
        string? literal,
        string? locKey,
        IReadOnlyList<string>? locArgs
    )
    {
        if (literal is not null)
        {
            writer.WriteString(propertyName, literal);

            return;
        }

        if (locKey is null)
        {
            return;
        }

        writer.WriteStartObject(propertyName);
        writer.WriteString("loc-key", locKey);
        _WriteOptionalArray(writer, "loc-args", locArgs);
        writer.WriteEndObject();
    }

    private static string _LiveActivityEventValue(ApnsLiveActivityEvent activityEvent)
    {
        return activityEvent switch
        {
            ApnsLiveActivityEvent.Start => "start",
            ApnsLiveActivityEvent.Update => "update",
            ApnsLiveActivityEvent.End => "end",
            _ => throw new ArgumentOutOfRangeException(
                nameof(activityEvent),
                activityEvent,
                "Unknown Live Activity event."
            ),
        };
    }

    private static void _EnsureJsonObject(JsonElement element, string description)
    {
        // ActivityKit decodes both values into Swift structs, which only a JSON object can populate.
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"A Live Activity {description} must be a JSON object, not {element.ValueKind}.",
                nameof(element)
            );
        }
    }

    #endregion

    #region Shared

    private static void _ValidateAlert(ApnsAlert alert, bool liveActivity)
    {
        _EnsureTextPair(alert.Title, alert.TitleLocKey, alert.TitleLocArgs, "title");
        _EnsureTextPair(alert.Subtitle, alert.SubtitleLocKey, alert.SubtitleLocArgs, "subtitle");
        _EnsureTextPair(alert.Body, alert.LocKey, alert.LocArgs, "body");

        var hasText =
            alert.Title is not null
            || alert.TitleLocKey is not null
            || alert.Subtitle is not null
            || alert.SubtitleLocKey is not null
            || alert.Body is not null
            || alert.LocKey is not null;

        if (!hasText)
        {
            throw new ArgumentException("An APNs alert needs a title, a subtitle, or a body.", nameof(alert));
        }

        if (
            liveActivity
            && (
                alert.Subtitle is not null
                || alert.SubtitleLocKey is not null
                || alert.SubtitleLocArgs is not null
                || alert.LaunchImage is not null
            )
        )
        {
            throw new ArgumentException(
                "A Live Activity alert shows only a title and a body; it cannot set a subtitle or a launch image.",
                nameof(alert)
            );
        }
    }

    private static void _EnsureTextPair(
        string? literal,
        string? locKey,
        IReadOnlyList<string>? locArgs,
        string description
    )
    {
        // Apple reads a localization key in place of the literal, so setting both leaves which one shows undefined.
        if (literal is not null && locKey is not null)
        {
            throw new ArgumentException(
                $"An APNs alert {description} sets both a literal and a localization key; set one.",
                nameof(literal)
            );
        }

        if (locArgs is not null && locKey is null)
        {
            throw new ArgumentException(
                $"An APNs alert {description} sets localization arguments without a localization key.",
                nameof(locArgs)
            );
        }
    }

    private static void _EnsureRelevanceScore(double score, string paramName)
    {
        // NaN matches no relational pattern, so it is refused here too.
        if (score is not (>= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                score,
                "An APNs alert relevance score must be from 0 to 1."
            );
        }
    }

    private static void _EnsureNoReservedKey(IReadOnlyDictionary<string, string>? data, string paramName)
    {
        if (data?.ContainsKey(_ApsKey) == true)
        {
            throw new ArgumentException($"Notification data contains the reserved APNs key '{_ApsKey}'.", paramName);
        }
    }

    private static void _EnsureWithinLimit(int payloadBytes, int limit, string pushType, string paramName)
    {
        if (payloadBytes > limit)
        {
            throw new ArgumentException(
                $"The APNs payload is {payloadBytes.ToString(CultureInfo.InvariantCulture)} bytes, over the {limit.ToString(CultureInfo.InvariantCulture)}-byte limit for {pushType} pushes.",
                paramName
            );
        }
    }

    private static void _WriteData(Utf8JsonWriter writer, IReadOnlyDictionary<string, string>? data)
    {
        if (data is null)
        {
            return;
        }

        foreach (var (key, value) in data)
        {
            writer.WriteString(key, value);
        }
    }

    private static void _WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void _WriteOptionalArray(Utf8JsonWriter writer, string propertyName, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        writer.WriteStartArray(propertyName);

        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    #endregion
}
