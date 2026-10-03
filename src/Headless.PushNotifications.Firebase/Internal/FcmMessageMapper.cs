// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using FirebaseAdmin.Messaging;

namespace Headless.PushNotifications.Firebase.Internal;

/// <summary>
/// Maps a validated <see cref="FcmMessage"/> onto the FirebaseAdmin SDK's <see cref="Message"/>. It is the only
/// place SDK message types are built, for the typed API and the shared request alike.
/// </summary>
internal static class FcmMessageMapper
{
    /// <summary>Builds the SDK message for one target.</summary>
    public static Message ToMessage(FcmMessage message, FcmTarget target)
    {
        var sdkMessage = _Template(message);

        switch (target.Kind)
        {
            case FcmTargetKind.Topic:
                sdkMessage.Topic = target.Value;
                break;
            case FcmTargetKind.Condition:
                sdkMessage.Condition = target.Value;
                break;
            default:
                sdkMessage.Fid = target.Value;
                break;
        }

        return sdkMessage;
    }

    /// <summary>
    /// Builds one SDK message per device, sharing the platform blocks, which the SDK copies before sending. A
    /// per-device list rather than the SDK's multicast message, because that type drops <c>fcm_options</c> and so the
    /// analytics label.
    /// </summary>
    public static List<Message> ToMessages(FcmMessage message, IEnumerable<string> fids)
    {
        var template = _Template(message);

        return
        [
            .. fids.Select(fid => new Message
            {
                Fid = fid,
                Data = template.Data,
                Notification = template.Notification,
                Android = template.Android,
                Webpush = template.Webpush,
                Apns = template.Apns,
                FcmOptions = template.FcmOptions,
            }),
        ];
    }

    private static Message _Template(FcmMessage message)
    {
        return new Message
        {
            Data = message.Data,
            Notification = message.Notification is { } notification
                ? new Notification
                {
                    Title = notification.Title,
                    Body = notification.Body,
                    ImageUrl = notification.Image?.AbsoluteUri,
                }
                : null,
            Android = message.Android is { } android ? _Android(android) : null,
            Webpush = message.Webpush is { } webpush ? _Webpush(webpush) : null,
            Apns = message.Apns is { } apns ? _Apns(apns) : null,
            FcmOptions = message.AnalyticsLabel is { } label ? new FcmOptions { AnalyticsLabel = label } : null,
        };
    }

    private static AndroidConfig _Android(FcmAndroidOptions android)
    {
        var hasNotification =
            android.ChannelId is not null
            || android.Tag is not null
            || android.Color is not null
            || android.Icon is not null
            || android.ClickAction is not null
            || android.Sound is not null
            || android.NotificationCount is not null
            || android.Visibility is not null
            || android.Image is not null;

        return new AndroidConfig
        {
            Priority = android.Priority switch
            {
                FcmAndroidPriority.High => Priority.High,
                FcmAndroidPriority.Normal => Priority.Normal,
                _ => null,
            },
            TimeToLive = android.TimeToLive,
            CollapseKey = android.CollapseKey,
            DirectBootOk = android.DirectBootOk,
            // Without a notification field the block is omitted, so a data message stays a data message on Android.
            Notification = hasNotification
                ? new AndroidNotification
                {
                    ChannelId = android.ChannelId,
                    Tag = android.Tag,
                    Color = android.Color,
                    Icon = android.Icon,
                    ClickAction = android.ClickAction,
                    Sound = android.Sound,
                    NotificationCount = android.NotificationCount,
                    Visibility = android.Visibility switch
                    {
                        FcmAndroidVisibility.Private => NotificationVisibility.PRIVATE,
                        FcmAndroidVisibility.Public => NotificationVisibility.PUBLIC,
                        FcmAndroidVisibility.Secret => NotificationVisibility.SECRET,
                        _ => null,
                    },
                    ImageUrl = android.Image?.AbsoluteUri,
                }
                : null,
        };
    }

    private static WebpushConfig _Webpush(FcmWebpushOptions webpush)
    {
        return new WebpushConfig
        {
            Headers = webpush.Headers,
            Data = webpush.Data,
            FcmOptions = webpush.Link is { } link ? new WebpushFcmOptions { Link = link.AbsoluteUri } : null,
        };
    }

    private static ApnsConfig _Apns(FcmApnsOptions apns)
    {
        return new ApnsConfig
        {
            Headers = apns.Headers,
            // The whole payload, aps included, goes through the SDK's custom data: its typed Aps cannot express every
            // APNs key, and the SDK accepts aps from custom data when its typed Aps is unset.
            CustomData = apns.Payload is { } payload ? _ToClr(payload) : null,
        };
    }

    /// <summary>
    /// Copies a JSON object into plain CLR dictionaries, lists, and primitives. The SDK serializes custom data with
    /// its own JSON library, which would write a <see cref="JsonNode"/>'s members rather than its JSON.
    /// </summary>
    private static Dictionary<string, object> _ToClr(JsonObject payload)
    {
        using var document = JsonDocument.Parse(payload.ToJsonString());

        return (Dictionary<string, object>)_ToClr(document.RootElement)!;
    }

    private static object? _ToClr(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(static p => p.Name, static p => _ToClr(p.Value)!, StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(_ToClr).ToList(),
            JsonValueKind.String => element.GetString(),
            // Boxed per branch so an integer stays an integer instead of widening to the decimal branch's type.
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? (object)integer
            : element.TryGetDecimal(out var number) ? (object)number
            : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}
