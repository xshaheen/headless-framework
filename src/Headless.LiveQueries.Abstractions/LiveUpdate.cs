// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.LiveQueries;

/// <summary>One update of a live query, in the form a transport forwards to a subscriber.</summary>
/// <remarks>
/// A run whose result equals the last one sent produces no update. The record grows through new
/// <see langword="init" /> properties, so construct it with an object initializer.
/// </remarks>
[PublicAPI]
public sealed record LiveUpdate
{
    /// <summary>Gets what the run produced.</summary>
    public required LiveUpdateKind Kind { get; init; }

    /// <summary>
    /// Gets the result as serialized UTF-8 JSON, or the failure body for <see cref="LiveUpdateKind.Failed" />. A
    /// transport forwards these bytes as they are.
    /// </summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>
    /// Gets an opaque token that identifies <see cref="Payload" /> within its subscription: two updates of one
    /// subscription carry the same token only when their payloads are equal.
    /// </summary>
    /// <remarks>
    /// Compare tokens for equality only. A token has no order, and tokens of different subscriptions are unrelated
    /// even for equal payloads, so a subscriber learns nothing about writes that other subscriptions saw.
    /// </remarks>
    public required string Version
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    }
}
