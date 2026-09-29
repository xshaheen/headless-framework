// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>
/// Thrown when an admitted attempt no longer owns its key, so its fenced writes or its completion must not commit.
/// Nothing was written; let it roll the unit of work back.
/// </summary>
[PublicAPI]
public sealed class StaleAdmissionException : InvalidOperationException
{
    /// <summary>Creates the exception for an attempt the store refused.</summary>
    /// <param name="key">The operation's tenant and key.</param>
    /// <param name="generation">The refused attempt's generation.</param>
    /// <param name="reason">What the store found; never <see cref="IdempotentLeaseStatus.Current" />.</param>
    public StaleAdmissionException(IdempotencyKey key, long generation, IdempotentLeaseStatus reason)
        : base(
            $"The idempotent operation '{key.Key}' attempt {generation} no longer owns its key ({reason}); roll the "
                + "unit of work back."
        )
    {
        Key = key;
        Generation = generation;
        Reason = reason;
    }

    /// <summary>Gets the operation's tenant and key.</summary>
    public IdempotencyKey Key { get; }

    /// <summary>Gets the refused attempt's generation.</summary>
    public long Generation { get; }

    /// <summary>Gets what the store found.</summary>
    public IdempotentLeaseStatus Reason { get; }
}
