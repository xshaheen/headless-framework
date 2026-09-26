// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
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

        if (notification is ApnsRawNotification raw)
        {
            return new ApnsPreparedNotification(_RawPayload(raw, headers), headers);
        }

        var buffer = new ArrayBufferWriter<byte>(512);

#pragma warning disable MA0045 // False positive: a synchronous in-memory JSON writer in a synchronous method; await using would add nothing.
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
                    _WriteEmptyApsWithData(writer, voipData.Data);
                    break;
                case ApnsLocationNotification location:
                    _WriteEmptyApsWithData(writer, location.Data);
                    break;
                case ApnsPushToTalkNotification pushToTalk:
                    _WriteEmptyApsWithData(writer, pushToTalk.Data);
                    break;
                case ApnsComplicationNotification complication:
                    _WriteEmptyApsWithData(writer, complication.Data);
                    break;
                case ApnsWidgetsNotification or ApnsControlsNotification:
                    _WriteContentChanged(writer);
                    break;
                case ApnsFileProviderNotification fileProvider:
                    _WriteFileProviderNotification(writer, fileProvider);
                    break;
                default:
                    throw new ArgumentException(
                        $"Unsupported APNs notification type '{notification.GetType().Name}'.",
                        nameof(notification)
                    );
            }
        }

        _EnsureWithinLimit(buffer.WrittenCount, headers.PushType, nameof(notification));

        return new ApnsPreparedNotification(buffer.WrittenSpan.ToArray(), headers);
    }

    #region Raw

    private static byte[] _RawPayload(ApnsRawNotification notification, ApnsRequestHeaders headers)
    {
        if (notification.Payload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"An APNs raw notification payload must be a JSON object, not {notification.Payload.ValueKind}.",
                nameof(notification)
            );
        }

        // The element's own UTF-8 bytes rather than a re-serialization, so the payload reaches APNs exactly as the
        // caller wrote it and the size check measures those bytes.
        var payload = JsonMarshal.GetRawUtf8Value(notification.Payload).ToArray();

        _EnsureWithinLimit(payload.Length, headers.PushType, nameof(notification));

        return payload;
    }

    #endregion

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

    #region Data-only push types

    private static void _WriteEmptyApsWithData(Utf8JsonWriter writer, JsonObject? data)
    {
        _EnsureNoReservedKey(data, "notification");

        // VoIP, location, push-to-talk, and complication pushes hand the whole payload to the app, but APNs still
        // expects the aps dictionary to be present.
        writer.WriteStartObject();
        writer.WriteStartObject(_ApsKey);
        writer.WriteEndObject();
        _WriteData(writer, data);
        writer.WriteEndObject();
    }

    private static void _WriteContentChanged(Utf8JsonWriter writer)
    {
        // Widget and control pushes carry no content of their own: the system reloads the timeline or the control.
        writer.WriteStartObject();
        writer.WriteStartObject(_ApsKey);
        writer.WriteBoolean("content-changed", value: true);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void _WriteFileProviderNotification(Utf8JsonWriter writer, ApnsFileProviderNotification notification)
    {
        Argument.IsNotNullOrWhiteSpace(notification.ContainerIdentifier, paramName: nameof(notification));
        Argument.IsNotNullOrWhiteSpace(notification.Domain, paramName: nameof(notification));

        // Apple's File Provider payload is these two top-level keys and no aps dictionary.
        writer.WriteStartObject();
        writer.WriteString("container-identifier", notification.ContainerIdentifier);
        writer.WriteString("domain", notification.Domain);
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

        if (notification.RequestPushToken)
        {
            writer.WriteNumber("input-push-token", 1);
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
        else if (notification.RequestPushToken)
        {
            // An update or end goes to the activity's own push token, so there is no new token to request.
            throw new ArgumentException(
                "Only a Live Activity 'start' push can request a push token.",
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

    private static void _EnsureNoReservedKey(JsonObject? data, string paramName)
    {
        if (data?.ContainsKey(_ApsKey) == true)
        {
            throw new ArgumentException($"Notification data contains the reserved APNs key '{_ApsKey}'.", paramName);
        }
    }

    private static void _EnsureWithinLimit(int payloadBytes, string pushType, string paramName)
    {
        var limit = string.Equals(pushType, ApnsPushTypes.Voip, StringComparison.Ordinal)
            ? _MaxVoipPayloadBytes
            : _MaxAlertPayloadBytes;

        if (payloadBytes > limit)
        {
            throw new ArgumentException(
                $"The APNs payload is {payloadBytes.ToString(CultureInfo.InvariantCulture)} bytes, over the {limit.ToString(CultureInfo.InvariantCulture)}-byte limit for {pushType} pushes.",
                paramName
            );
        }
    }

    private static void _WriteData(Utf8JsonWriter writer, JsonObject? data)
    {
        if (data is null)
        {
            return;
        }

        // Each value is written into this payload's writer rather than moved into a new tree, so the caller's
        // object keeps its nodes and can be reused, including by concurrent sends.
        foreach (var (key, value) in data)
        {
            writer.WritePropertyName(key);

            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                value.WriteTo(writer);
            }
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
