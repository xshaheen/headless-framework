// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>Which fenced transition a consumer's write composes into.</summary>
internal enum FencedTransitionKind : byte
{
    /// <summary>The consumer's assignments alone, fenced on the current generation.</summary>
    Write,

    /// <summary>The consumer's assignments and a renewal, fenced on the current generation.</summary>
    WriteAndRenew,

    /// <summary>Ends the current generation as settled, with the consumer's assignments when given.</summary>
    Settle,

    /// <summary>Ends the current generation as released, with the consumer's assignments when given.</summary>
    Release,

    /// <summary>Ends whatever attempt is active as revoked, with the consumer's assignments.</summary>
    Revoke,
}
