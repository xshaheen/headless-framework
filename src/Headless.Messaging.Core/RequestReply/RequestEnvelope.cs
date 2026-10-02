// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;

namespace Headless.Messaging.RequestReply;

/// <summary>
/// Reads the request headers a responder acts on. Only a message that names a reply address is a request: every other
/// Queue message keeps the ordinary consume path, whatever else it carries.
/// </summary>
internal static class RequestEnvelope
{
    /// <summary>Whether <paramref name="headers"/> belong to a request whose caller awaits a reply.</summary>
    public static bool IsRequest(IDictionary<string, string?> headers)
    {
        return headers.TryGetValue(Headers.ReplyTo, out var replyTo) && !string.IsNullOrWhiteSpace(replyTo);
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
    public static bool IsExpired(IDictionary<string, string?> headers, DateTimeOffset now)
    {
        return IsRequest(headers) && GetDeadline(headers) is { } deadline && now >= deadline;
    }
}
