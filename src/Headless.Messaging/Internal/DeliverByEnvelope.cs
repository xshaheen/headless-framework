// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Reads the delivery deadline a publisher stamped from <c>MessageOptions.DeliverWithin</c>. Unlike
/// <see cref="RequestReply.RequestEnvelope"/>, it applies to both lanes and to plain messages, not only requests.
/// </summary>
internal static class DeliverByEnvelope
{
    /// <summary>
    /// The instant after which the message is dropped instead of delivered, or <see langword="null"/> when it carries
    /// no readable deadline. An unreadable deadline is ignored rather than treated as expired, so a malformed value
    /// from a foreign publisher cannot silently discard every message carrying it.
    /// </summary>
    public static DateTimeOffset? GetDeadline(IDictionary<string, string?> headers)
    {
        if (!headers.TryGetValue(Headers.DeliverBy, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var deadline
        )
            ? deadline
            : null;
    }

    /// <summary>
    /// Whether the message's delivery deadline passed by <paramref name="now"/>, read on this host's clock. Clock skew
    /// between the publishing and receiving hosts shifts the window by the skew.
    /// </summary>
    public static bool IsExpired(IDictionary<string, string?> headers, DateTimeOffset now)
    {
        return GetDeadline(headers) is { } deadline && now >= deadline;
    }
}
