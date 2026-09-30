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

/// <summary>Attribute names the Firebase provider puts on its metrics and spans.</summary>
[PublicAPI]
public static class FcmTags
{
    /// <summary>
    /// The setup-builder instance name the send went through, or <see cref="FcmDiagnostics.DefaultInstanceName"/> for
    /// the unkeyed default. Bounded by the instances the host registers.
    /// </summary>
    public const string Instance = "headless.fcm.instance";

    /// <summary>The final outcome: <c>succeeded</c>, <c>unregistered</c>, or <c>failed</c>.</summary>
    public const string Outcome = "headless.fcm.outcome";

    /// <summary>The <see cref="FcmFailureKind"/> in lower snake case, such as <c>server_error</c>; absent on success.</summary>
    public const string FailureKind = "headless.fcm.failure_kind";

    /// <summary>
    /// The wire error code (<see cref="FcmSendResult.ErrorCode"/>), or <c>none</c> when no answer arrived; absent on
    /// success. Bounded: it comes from FCM's and Google's fixed error-code tables.
    /// </summary>
    public const string ErrorCode = "headless.fcm.error_code";

    /// <summary>What the send addressed: <c>token</c>, <c>topic</c>, or <c>condition</c>.</summary>
    public const string TargetKind = "headless.fcm.target_kind";

    /// <summary>The request shape a duration measures: <c>send</c> for one target or <c>multicast</c> for one round.</summary>
    public const string Operation = "headless.fcm.operation";

    /// <summary>Span only: whether the send was a dry run (<c>validate_only</c>).</summary>
    public const string DryRun = "headless.fcm.dry_run";

    /// <summary>Span only: how many in-process retries a send made, or which retry round a multicast round is (0 first).</summary>
    public const string Retry = "headless.fcm.retry";

    /// <summary>Span only: how many devices a multicast round addressed.</summary>
    public const string BatchSize = "headless.fcm.batch.size";

    /// <summary>Span only: how many devices of a multicast round FCM accepted.</summary>
    public const string SuccessCount = "headless.fcm.batch.success_count";
}
