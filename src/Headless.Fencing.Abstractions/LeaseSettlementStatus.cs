// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>The outcome of settling or releasing a lease by its generation.</summary>
/// <remarks>
/// Settling is the attempt's final write and reports <see cref="Settled" /> on success; releasing gives the lease up
/// without a result and reports <see cref="Released" /> on success. Both are idempotent for the same generation, so a
/// retried call that already succeeded reports success again. Every other value is a refusal: nothing was written.
/// </remarks>
[PublicAPI]
public enum LeaseSettlementStatus
{
    /// <summary>The attempt settled, now or by an earlier call with the same generation.</summary>
    Settled = 0,

    /// <summary>A newer grant replaced this generation, or the lease no longer exists.</summary>
    Stale = 1,

    /// <summary>
    /// The generation is current but the lease expired, so the attempt no longer owns its outcome: a takeover or a
    /// sweep does.
    /// </summary>
    Expired = 2,

    /// <summary>The attempt was released, now or by an earlier call with the same generation.</summary>
    Released = 3,

    /// <summary>A sweep abandoned this generation's expired attempt.</summary>
    Abandoned = 4,
}
