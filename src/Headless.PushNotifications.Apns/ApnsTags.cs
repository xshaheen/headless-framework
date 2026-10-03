// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

namespace Headless.PushNotifications.Apns;

/// <summary>Well-known APNs tag names emitted by the framework on metric instruments and activity spans.</summary>
[PublicAPI]
public static class ApnsTags
{
    /// <summary>Send outcome: <c>succeeded</c>, <c>unregistered</c>, or <c>failed</c>.</summary>
    public const string Outcome = "headless.apns.outcome";

    /// <summary>
    /// Failure category on a failed send; the <see cref="ApnsFailureKind"/> name in lower snake case, or
    /// <see langword="null"/>-omitted on success.
    /// </summary>
    public const string FailureKind = "headless.apns.failure_kind";

    /// <summary>The APNs <c>reason</c> the rejection carried, or <c>none</c> when there was no answer.</summary>
    /// <remarks>
    /// Bounded in practice: it comes from APNs' documented response error string table, and an unknown reason is a
    /// rare forward-compatibility case, not caller input.
    /// </remarks>
    public const string Reason = "headless.apns.reason";

    /// <summary>The <c>apns-push-type</c> of the sent notification, such as <c>alert</c> or <c>voip</c>.</summary>
    public const string PushType = "headless.apns.push_type";

    /// <summary>The environment the instance delivers to: <c>production</c> or <c>sandbox</c>.</summary>
    public const string Environment = "headless.apns.environment";
}
