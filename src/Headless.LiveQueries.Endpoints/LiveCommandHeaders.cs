// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries;

/// <summary>
/// The HTTP headers of command acknowledgement: what a client sends so that its own streams report when their
/// result holds one of its commands, and what the command's response says back.
/// </summary>
/// <remarks>
/// A browser client on another origin needs the two request headers allowed and
/// <see cref="Acknowledgement" /> exposed by the host's CORS policy.
/// </remarks>
[PublicAPI]
public static class LiveCommandHeaders
{
    /// <summary>
    /// Request header, on a stream and on a command: an id the client chose for itself, of 22 to 64 ASCII letters,
    /// digits and hyphens, random enough not to be guessed. A value of any other shape is ignored.
    /// </summary>
    public const string Session = "X-Headless-Live-Session";

    /// <summary>
    /// Request header, on a command: an id the client chose for this command, of 1 to 64 ASCII letters, digits and
    /// hyphens. A value of any other shape is ignored, and so is the header on a request without a usable
    /// <see cref="Session" />.
    /// </summary>
    public const string Command = "X-Headless-Live-Command";

    /// <summary>
    /// Response header of a command whose <see cref="Session" /> and <see cref="Command" /> were accepted:
    /// <see cref="AcknowledgementPending" /> or <see cref="AcknowledgementNone" />. Absent when either was missing
    /// or ignored, and then no stream will acknowledge the command.
    /// </summary>
    public const string Acknowledgement = "X-Headless-Live-Acknowledgement";

    /// <summary>
    /// <see cref="Acknowledgement" /> value: the command committed a tracked write before its response started, so
    /// each of the client's open streams reports the command id.
    /// </summary>
    public const string AcknowledgementPending = "pending";

    /// <summary>
    /// <see cref="Acknowledgement" /> value: the command had announced no write when its response started, so no
    /// stream will report the command id.
    /// </summary>
    public const string AcknowledgementNone = "none";
}
