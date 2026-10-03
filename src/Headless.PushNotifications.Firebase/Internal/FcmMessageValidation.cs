// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Headless.Checks;

namespace Headless.PushNotifications.Firebase.Internal;

/// <summary>
/// The client-side rules of an FCM send: the ones the FirebaseAdmin SDK would otherwise enforce only inside the send,
/// where a violation would come back as a per-target failure instead of an <see cref="ArgumentException"/> before any
/// request, plus the target rules FCM documents.
/// </summary>
/// <remarks>
/// Payload size is deliberately not checked: FCM measures it after its own encoding and rejects an oversized message
/// with <c>INVALID_ARGUMENT</c>, which is the authority.
/// </remarks>
internal static partial class FcmMessageValidation
{
    private const string _TopicsPrefix = "/topics/";

    // FCM's documented limit on the topics one condition may name.
    private const int _MaxConditionTopics = 5;

    /// <summary>Throws unless <paramref name="message"/> can be sent as written.</summary>
    public static void Validate(FcmMessage message)
    {
        Argument.IsNotNull(message);
        EnsureDataAllowed(message.Data, "message.Data");

        if (message.Notification?.Image is { } image)
        {
            _EnsureAbsolute(image, "message.Notification.Image");
        }

        if (message.Android is { } android)
        {
            _ValidateAndroid(android);
        }

        if (message.Webpush?.Link is { } link)
        {
            // The SDK requires an absolute HTTPS link and rejects anything else inside the send.
            Argument.IsTrue(
                link.IsAbsoluteUri && string.Equals(link.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal),
                "A web push link must be an absolute HTTPS URL.",
                "message.Webpush.Link"
            );
        }

        if (message.Apns?.Payload is { } payload)
        {
            // FCM requires an aps dictionary in every APNs payload; the SDK rejects a payload without one.
            Argument.IsTrue(
                payload["aps"] is JsonObject,
                "An APNs payload must hold an 'aps' object.",
                "message.Apns.Payload"
            );
        }

        if (message.AnalyticsLabel is { } label)
        {
            Argument.Matches(
                label,
                _AnalyticsLabelRegex,
                "An FCM analytics label must match ^[a-zA-Z0-9-_.~%]{1,50}$.",
                "message.AnalyticsLabel"
            );
        }
    }

    /// <summary>Throws unless <paramref name="topic"/> is a bare FCM topic name.</summary>
    public static void ValidateTopic(string topic)
    {
        Argument.IsNotNullOrWhiteSpace(topic);

        // The SDK strips the prefix silently; rejecting it keeps one spelling of a topic in callers' stores.
        Argument.IsFalse(
            topic.StartsWith(_TopicsPrefix, StringComparison.Ordinal),
            "Pass the topic name without the '/topics/' prefix.",
            nameof(topic)
        );

        Argument.Matches(topic, _TopicRegex, "An FCM topic name must match [a-zA-Z0-9-_.~%]+.");
    }

    /// <summary>Throws unless <paramref name="condition"/> names between one and five valid topics.</summary>
    public static void ValidateCondition(string condition)
    {
        Argument.IsNotNullOrWhiteSpace(condition);

        var topics = _ConditionTopicRegex.Matches(condition);

        Argument.IsTrue(topics.Count > 0, "An FCM condition must name at least one topic.", nameof(condition));
        Argument.IsTrue(
            topics.Count <= _MaxConditionTopics,
            $"An FCM condition may name at most {_MaxConditionTopics} topics.",
            nameof(condition)
        );

        foreach (Match topic in topics)
        {
            Argument.IsTrue(
                _TopicRegex.IsMatch(topic.Groups["topic"].Value),
                "Every topic an FCM condition names must match [a-zA-Z0-9-_.~%]+.",
                nameof(condition)
            );
        }
    }

    /// <summary>
    /// Throws when <paramref name="data"/> uses a key FCM reserves: <c>from</c>, <c>message_type</c>, and the
    /// <c>google.</c> and <c>gcm.</c> namespaces, plus <c>notification</c>, which the legacy API reserved. A key that
    /// merely starts with <c>google</c> or <c>gcm</c> belongs to the app.
    /// </summary>
    public static void EnsureDataAllowed(IReadOnlyDictionary<string, string>? data, string paramName)
    {
        if (data is null)
        {
            return;
        }

        foreach (var key in data.Keys)
        {
            if (
                key is "from" or "notification" or "message_type"
                || key.StartsWith("google.", StringComparison.Ordinal)
                || key.StartsWith("gcm.", StringComparison.Ordinal)
            )
            {
                throw new ArgumentException($"Notification data contains the reserved FCM key '{key}'.", paramName);
            }
        }
    }

    private static void _ValidateAndroid(FcmAndroidOptions android)
    {
        if (android.Priority is { } priority)
        {
            Argument.IsInEnum(priority, paramName: "message.Android.Priority");
        }

        if (android.Visibility is { } visibility)
        {
            Argument.IsInEnum(visibility, paramName: "message.Android.Visibility");
        }

        if (android.TimeToLive is { } timeToLive)
        {
            Argument.IsPositiveOrZero(timeToLive, paramName: "message.Android.TimeToLive");
            Argument.IsLessThanOrEqualTo(
                timeToLive,
                PushNotificationRequestValidation.MaxTimeToLive,
                "An FCM time-to-live cannot exceed 28 days.",
                "message.Android.TimeToLive"
            );
        }

        if (android.NotificationCount is { } count)
        {
            Argument.IsPositiveOrZero(count, paramName: "message.Android.NotificationCount");
        }

        if (android.Color is { } color)
        {
            Argument.Matches(color, _ColorRegex, "An Android color must be #RRGGBB.", "message.Android.Color");
        }

        if (android.Image is { } image)
        {
            _EnsureAbsolute(image, "message.Android.Image");
        }
    }

    private static void _EnsureAbsolute(Uri uri, string paramName)
    {
        Argument.IsTrue(uri.IsAbsoluteUri, "An FCM image must be an absolute URL.", paramName);
    }

    [GeneratedRegex("^[a-zA-Z0-9-_.~%]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _TopicRegex { get; }

    [GeneratedRegex("^[a-zA-Z0-9-_.~%]{1,50}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _AnalyticsLabelRegex { get; }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex _ColorRegex { get; }

    // A condition names each topic as a quoted literal followed by "in topics", in single or double quotes.
    [GeneratedRegex(
        """(?<quote>['"])(?<topic>[^'"]*)\k<quote>\s+in\s+topics""",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex _ConditionTopicRegex { get; }
}
