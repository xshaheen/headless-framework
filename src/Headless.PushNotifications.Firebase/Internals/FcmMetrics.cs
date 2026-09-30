// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>
/// The Firebase provider's metric instruments on <see cref="FcmDiagnostics.Meter"/>. Instrument and attribute names
/// are bespoke <c>headless.fcm.*</c>, because no OpenTelemetry semantic convention covers push delivery.
/// </summary>
/// <remarks>
/// Each record method reads the instrument's <c>Enabled</c> flag first, so an unobserved provider builds no
/// <see cref="TagList"/>. Tokens, topics, conditions, and message content are never tags.
/// </remarks>
internal static class FcmMetrics
{
    internal const string SendsName = "headless.fcm.sends";
    internal const string SendDurationName = "headless.fcm.send.duration";
    internal const string RetriesName = "headless.fcm.retries";

    private static readonly Counter<long> _Sends = FcmDiagnostics.Meter.CreateCounter<long>(
        SendsName,
        unit: "{send}",
        description: "FCM sends per target, after retries, by outcome, failure kind, error code, and target kind."
    );

    private static readonly Histogram<double> _SendDuration = FcmDiagnostics.Meter.CreateHistogram<double>(
        SendDurationName,
        unit: "s",
        description: "Duration of one request round to FCM: a single send attempt or one multicast round."
    );

    private static readonly Counter<long> _Retries = FcmDiagnostics.Meter.CreateCounter<long>(
        RetriesName,
        unit: "{retry}",
        description: "In-process FCM resends of one target, by the failure that caused them."
    );

    /// <summary>Counts one final outcome.</summary>
    public static void RecordSend(string instance, FcmSendResult result, FcmTargetKind targetKind)
    {
        if (!_Sends.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { FcmTags.Instance, instance },
            { FcmTags.Outcome, OutcomeTag(result.Response.Status) },
            { FcmTags.TargetKind, FcmTarget.ToTagValue(targetKind) },
        };

        _AddFailureTags(ref tags, result);
        _Sends.Add(1, tags);
    }

    /// <summary>Records how long one request round took.</summary>
    public static void RecordDuration(string instance, TimeSpan duration, FcmTargetKind targetKind, bool multicast)
    {
        if (!_SendDuration.Enabled)
        {
            return;
        }

        _SendDuration.Record(
            duration.TotalSeconds,
            new TagList
            {
                { FcmTags.Instance, instance },
                { FcmTags.TargetKind, FcmTarget.ToTagValue(targetKind) },
                { FcmTags.Operation, multicast ? "multicast" : "send" },
            }
        );
    }

    /// <summary>Counts one resend of one target, tagged with the failure being retried.</summary>
    public static void RecordRetry(string instance, FcmSendResult failure, FcmTargetKind targetKind)
    {
        if (!_Retries.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { FcmTags.Instance, instance },
            { FcmTags.TargetKind, FcmTarget.ToTagValue(targetKind) },
        };

        _AddFailureTags(ref tags, failure);
        _Retries.Add(1, tags);
    }

    /// <summary>The <see cref="FcmTags.Outcome"/> value.</summary>
    internal static string OutcomeTag(PushNotificationResponseStatus status)
    {
        return status switch
        {
            PushNotificationResponseStatus.Success => "succeeded",
            PushNotificationResponseStatus.Unregistered => "unregistered",
            _ => "failed",
        };
    }

    /// <summary>The <see cref="FcmTags.FailureKind"/> value, in lower snake case.</summary>
    internal static string ToTagValue(FcmFailureKind kind)
    {
        return kind switch
        {
            FcmFailureKind.TokenInvalid => "token_invalid",
            FcmFailureKind.Throttled => "throttled",
            FcmFailureKind.ServerError => "server_error",
            FcmFailureKind.Authentication => "authentication",
            FcmFailureKind.Configuration => "configuration",
            FcmFailureKind.Payload => "payload",
            FcmFailureKind.Transport => "transport",
            _ => "unknown",
        };
    }

    private static void _AddFailureTags(ref TagList tags, FcmSendResult result)
    {
        if (result.FailureKind is { } kind)
        {
            tags.Add(FcmTags.FailureKind, ToTagValue(kind));
            tags.Add(FcmTags.ErrorCode, result.ErrorCode ?? "none");
        }
    }
}
