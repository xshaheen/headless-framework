// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AmbientTransactions;

/// <summary>
/// Buffers work until an ambient transaction is committed.
/// </summary>
[PublicAPI]
public interface IAmbientWorkBuffer<in TWork>
{
    /// <summary>
    /// Buffers work for the current ambient transaction.
    /// </summary>
    void Buffer(TWork work);
}
