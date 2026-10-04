// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Reads the request headers a responder acts on. Only a Queue message that names a reply address is a request: every
/// other Queue message, and every Bus message whatever headers it carries, keeps the ordinary consume path.
/// </summary>
internal static class RequestEnvelope
{
    /// <summary>
    /// Whether a message on <paramref name="lane"/> with <paramref name="headers"/> is a request whose caller awaits a
    /// reply. Requests are sent only on the Queue lane, so request headers on a Bus message, such as one a foreign
    /// publisher wrote, make it no request.
    /// </summary>
    public static bool IsRequest(MessageLane lane, IDictionary<string, string?> headers)
    {
        return lane is MessageLane.Queue
            && headers.TryGetValue(Headers.ReplyTo, out var replyTo)
            && !string.IsNullOrWhiteSpace(replyTo);
    }

    /// <summary>
    /// The instant the caller stops waiting, or <see langword="null"/> when the request carries no readable deadline. An
    /// unreadable deadline is ignored rather than treated as expired: the caller's own timer still bounds the call.
    /// </summary>
    public static DateTimeOffset? GetDeadline(IDictionary<string, string?> headers)
    {
        if (!headers.TryGetValue(Headers.RequestDeadline, out var value) || string.IsNullOrWhiteSpace(value))
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
    /// Whether the request's caller stopped waiting by <paramref name="now"/>, read on this host's clock. Clock skew
    /// between the two hosts shifts the window by the skew.
    /// </summary>
    public static bool IsExpired(MessageLane lane, IDictionary<string, string?> headers, DateTimeOffset now)
    {
        return IsRequest(lane, headers) && GetDeadline(headers) is { } deadline && now >= deadline;
    }
}
