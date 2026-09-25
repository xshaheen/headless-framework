// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns.Internals;

/// <summary>
/// A data-only shared request sent through a VoIP instance: push type <c>voip</c>, written as an empty <c>aps</c>
/// dictionary plus custom data.
/// </summary>
/// <remarks>
/// Internal because only the shared path needs it: PushKit never receives a <c>background</c> push, so a data-only
/// request on a VoIP instance stays a VoIP push, and the typed API expresses that as an alert notification instead.
/// </remarks>
internal sealed record ApnsVoipDataNotification : ApnsNotification
{
    /// <summary>Custom keys written beside <c>aps</c> at the payload's top level. The key <c>aps</c> is reserved.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>The delivery priority, or <see langword="null"/> to use <see cref="ApnsOptions.Priority"/>.</summary>
    public ApnsPriority? Priority { get; init; }
}
