// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>The stored state of a lease kept as columns on the consumer's own row.</summary>
/// <remarks>
/// The values are persisted, so they never change. They differ from the lease table's, where Active is 0, because the
/// state column's default of 0 must mean that the row was never granted.
/// </remarks>
[PublicAPI]
#pragma warning disable CA1028 // The state is stored in a smallint column, as the lease table's is; short reads it without a conversion.
public enum RowLeaseState : short
#pragma warning restore CA1028
{
    /// <summary>The row was never granted. A fresh row, whose lease columns hold their defaults, is idle.</summary>
    Idle = 0,

    /// <summary>An attempt holds the row until its expiry, by the database clock.</summary>
    Active = 1,

    /// <summary>The last attempt settled: its work finished.</summary>
    Settled = 2,

    /// <summary>The last attempt released the row without finishing its work.</summary>
    Released = 3,

    /// <summary>A sweep abandoned the last attempt after it expired.</summary>
    Abandoned = 4,

    /// <summary>Code that did not hold the row ended the last attempt.</summary>
    Revoked = 5,
}
