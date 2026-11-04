// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>
/// Reads the delivery deadline a publisher stamped from <c>MessageOptions.DeliverWithin</c>. Unlike
/// a request's own deadline, it applies to both lanes and to plain messages, not only requests.
/// </summary>
/// <remarks>
/// A consumer client whose broker can drop or dead-letter an expired message natively reads the deadline here and maps
/// it onto the broker's time-to-live; the core checks it again on receipt, so a client that maps nothing still drops an
/// expired message.
/// </remarks>
public static class DeliverByEnvelope
{
    /// <summary>
    /// The instant after which the message is dropped instead of delivered, or <see langword="null"/> when it carries
    /// no readable deadline. An unreadable deadline is ignored rather than treated as expired, so a malformed value
    /// from a foreign publisher cannot silently discard every message carrying it.
    /// </summary>
    /// <param name="headers">The message headers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="headers"/> is <see langword="null"/>.</exception>
    public static DateTimeOffset? GetDeadline(IDictionary<string, string?> headers)
    {
        Argument.IsNotNull(headers);

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
    /// <param name="headers">The message headers.</param>
    /// <param name="now">The current time on this host's clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="headers"/> is <see langword="null"/>.</exception>
    public static bool IsExpired(IDictionary<string, string?> headers, DateTimeOffset now)
    {
        return GetDeadline(headers) is { } deadline && now >= deadline;
    }
}
