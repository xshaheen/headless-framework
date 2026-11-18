// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>
/// One client session of one user: who holds a subscription, and who sent a command. A command is acknowledged to
/// the subscriptions whose subscriber equals its sender, and to no other.
/// </summary>
internal sealed record LiveSubscriber
{
    // 128 bits when the id is base64url: the session id is all that separates two clients of one user, and all
    // that separates two anonymous clients, so it must be too long to guess.
    private const int _MinSessionLength = 22;

    private LiveSubscriber(string session, string? user)
    {
        Session = session;
        User = user;
    }

    /// <summary>Gets the id the client chose for itself and sends on its streams and its commands.</summary>
    public string Session { get; }

    /// <summary>Gets the user the caller resolved, or <see langword="null" /> for an anonymous client.</summary>
    public string? User { get; }

    /// <summary>
    /// Returns the subscriber, or <see langword="null" /> when <paramref name="session" /> is not a token of at
    /// least 22 characters. A client without a usable session id simply takes no part in acknowledgement.
    /// </summary>
    public static LiveSubscriber? TryCreate(string? session, string? user)
    {
        return LiveToken.IsValid(session, _MinSessionLength) ? new LiveSubscriber(session, user) : null;
    }
}
