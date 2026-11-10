// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// One granted attempt on a lease kept as columns on the consumer's own row: the table, the row's key, and the
/// generation the grant issued. The generation is the fencing token every later renew, settle, release, write, or
/// fence names.
/// </summary>
/// <remarks>
/// A separate type from <see cref="FencedLease" />, so a row token can never reach the lease table, which would read
/// the wrong row and answer <see cref="LeaseFenceStatus.Stale" />. Holding one proves nothing by itself; only the
/// row's answer does.
/// </remarks>
/// <typeparam name="TKey">The row's key type: a scalar such as <see cref="Guid" />, or a record for a composite key.</typeparam>
/// <param name="Table">The name of the table the row lives in, as the table descriptor reports it.</param>
/// <param name="Key">The row's key.</param>
/// <param name="Generation">The generation the grant issued.</param>
[PublicAPI]
public sealed record FencedRow<TKey>(string Table, TKey Key, long Generation)
    where TKey : notnull;
