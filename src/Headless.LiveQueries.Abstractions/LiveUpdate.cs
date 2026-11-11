// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.LiveQueries;

/// <summary>One update of a live query, in the form a transport forwards to a subscriber.</summary>
/// <remarks>
/// A run whose result equals the last one sent produces no update, unless the subscription has commands to
/// acknowledge, which then travel alone as <see cref="LiveUpdateKind.Acknowledged" />. The record grows through new
/// <see langword="init" /> properties, so construct it with an object initializer.
/// </remarks>
[PublicAPI]
public sealed record LiveUpdate
{
    /// <summary>Gets what the run produced.</summary>
    public required LiveUpdateKind Kind { get; init; }

    /// <summary>
    /// Gets the result as serialized UTF-8 JSON, or the failure body for <see cref="LiveUpdateKind.Failed" />. A
    /// transport forwards these bytes as they are. Empty for <see cref="LiveUpdateKind.Acknowledged" />, which
    /// carries no result.
    /// </summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>
    /// Gets an opaque token that identifies <see cref="Payload" /> within its subscription: two updates of one
    /// subscription carry the same token only when their payloads are equal.
    /// </summary>
    /// <remarks>
    /// Compare tokens for equality only. A token has no order, and tokens of different subscriptions are unrelated
    /// even for equal payloads, so a subscriber learns nothing about writes that other subscriptions saw. An
    /// <see cref="LiveUpdateKind.Acknowledged" /> update repeats the token of the last result sent.
    /// </remarks>
    public required string Version
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    }

    /// <summary>
    /// Gets the ids of the subscriber's own commands whose committed writes the subscription's current result
    /// already reflects: the result in <see cref="Payload" />, or for <see cref="LiveUpdateKind.Acknowledged" />
    /// the last result sent. Empty for most updates.
    /// </summary>
    /// <remarks>
    /// Each id is a token of ASCII letters, digits and hyphens, at most 64 characters, so a transport may write it
    /// to the wire unescaped. Only commands sent by the client session and user that hold the subscription appear
    /// here; a subscription opened without a session never has any.
    /// </remarks>
    public IReadOnlyList<string> AcknowledgedCommands
    {
        get;
        init => field = Argument.IsNotNull(value);
    } = [];
}
