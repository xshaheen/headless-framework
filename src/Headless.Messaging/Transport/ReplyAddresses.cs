// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// The reserved namespace every reply address lives in, shared by all <see cref="IReplyTransport"/> implementations.
/// </summary>
/// <remarks>
/// Holding every address to one prefix and a narrow character set lets a responding host refuse a forged destination
/// before any broker call. Keep application message names out of <see cref="Prefix"/>: a lane destination under it
/// could receive a forged reply.
/// </remarks>
[PublicAPI]
public static class ReplyAddresses
{
    /// <summary>The prefix every reply address starts with. Value: <c>"headless.reply."</c>.</summary>
    public const string Prefix = "headless.reply.";

    /// <summary>The longest reply address accepted, which keeps every address within common broker name limits.</summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Creates a new, unguessable reply address. A provider may build its own address instead, as long as
    /// <see cref="IsInReplyNamespace"/> accepts it.
    /// </summary>
    /// <returns>An address of the form <c>headless.reply.{32 hex digits}</c>.</returns>
    public static string Create()
    {
        // A random identifier rather than a time-ordered one, so a reply address cannot be guessed from another.
        return Prefix + Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Returns whether <paramref name="address"/> starts with <see cref="Prefix"/>, has a non-empty remainder made only
    /// of ASCII letters, digits, <c>'.'</c>, <c>'-'</c>, and <c>'_'</c>, and is at most <see cref="MaxLength"/>
    /// characters.
    /// </summary>
    /// <remarks>
    /// The character set excludes wildcards and separators that some brokers interpret, such as <c>'*'</c>, <c>'&gt;'</c>,
    /// <c>'#'</c>, and whitespace, so a forged address cannot fan out to other subscribers.
    /// </remarks>
    /// <param name="address">The address to check.</param>
    public static bool IsInReplyNamespace([NotNullWhen(true)] string? address)
    {
        if (
            address is null
            || address.Length <= Prefix.Length
            || address.Length > MaxLength
            || !address.StartsWith(Prefix, StringComparison.Ordinal)
        )
        {
            return false;
        }

        foreach (var c in address.AsSpan(Prefix.Length))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
