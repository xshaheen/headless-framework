// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>How a grant treats an expired attempt, and what it records about the attempt it starts.</summary>
[PublicAPI]
public sealed class LeaseGrantOptions
{
    /// <summary>
    /// Gets whether an expired attempt is taken over at once, or kept until a sweep abandons it. Defaults to
    /// <see cref="LeaseTakeover.Allowed" />; pass <see cref="LeaseTakeover.AfterSweep" /> for work that may already
    /// have had an external effect.
    /// </summary>
    public LeaseTakeover Takeover { get; init; } = LeaseTakeover.Allowed;

    /// <summary>
    /// Gets the owner tag stored with the attempt this grant starts, such as <c>cluster/node@incarnation</c>, or
    /// <see langword="null" /> to store none.
    /// </summary>
    /// <remarks>
    /// A grant whose holder equals (ordinally) the stored holder of the live attempt returns that attempt as
    /// <see cref="LeaseGrantStatus.Granted" />, with its generation and expiry and without writing anything, instead of
    /// <see cref="LeaseGrantStatus.Held" />. That makes a grant retried after a lost response idempotent. The expiry is
    /// not extended; renew to extend it. Any other holder, and a grant without one, still gets
    /// <see cref="LeaseGrantStatus.Held" />. At most <c>FencingFieldLimits.HolderMaxLength</c> characters, and never
    /// empty or whitespace.
    /// </remarks>
    public string? Holder { get; init; }

    /// <summary>
    /// Gets the attributes stored with the attempt this grant starts, or <see langword="null" /> to store none. They
    /// stay with that generation until a later grant replaces it.
    /// </summary>
    public LeaseAttributes? Attributes { get; init; }
}
