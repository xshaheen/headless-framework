// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Names the <see cref="ActivitySource"/> and meter the Firebase provider emits through. Subscribe with
/// <see cref="SourceName"/> (<c>AddSource</c> and <c>AddMeter</c>); the span and metric attribute names are in
/// <see cref="FcmTags"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every metric and span carries <see cref="FcmTags.Instance"/>, the setup-builder instance name, so named Firebase
/// projects can be told apart. Metrics: <c>headless.fcm.sends</c> counts one final outcome per target, after retries;
/// <c>headless.fcm.send.duration</c> (seconds) times each request round to FCM, a single send attempt or one
/// multicast round; <c>headless.fcm.retries</c> counts each in-process resend of one target.
/// </para>
/// <para>
/// Traces: one <c>fcm.send</c> activity per device, topic, or condition send, spanning its retries, and one
/// <c>fcm.multicast</c> activity per multicast round of at most 500 devices. Tokens, topic and condition text, and
/// message content are never tags: a token is a stable device identifier and the rest is caller data.
/// </para>
/// </remarks>
[PublicAPI]
public static class FcmDiagnostics
{
    /// <summary>The activity-source and meter name (<c>Headless.PushNotifications.Firebase</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "PushNotifications.Firebase";

    /// <summary>The <see cref="FcmTags.Instance"/> value of the unkeyed default instance.</summary>
    public const string DefaultInstanceName = "default";

    /// <summary>Shared <see cref="ActivitySource"/> for FCM send traces.</summary>
    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource(
        "PushNotifications.Firebase"
    );

    /// <summary>Shared <see cref="Meter"/> for FCM send metrics.</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("PushNotifications.Firebase");
}
