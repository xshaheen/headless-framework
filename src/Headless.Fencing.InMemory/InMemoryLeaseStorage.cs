// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing.InMemory;

/// <summary>
/// The process-local state behind the in-memory lease store: the lease table and the store-wide generation counter.
/// A singleton, so it lives and dies with the process.
/// </summary>
internal sealed class InMemoryLeaseStorage : IDisposable
{
    private long _lastGeneration;

    /// <summary>Gets the committed lease rows.</summary>
    public InMemoryRowTable<LeaseKey, InMemoryLease> Table { get; } = new(InMemoryLeaseStore.LockName);

    /// <summary>
    /// Draws the next generation. One counter for every key, never reset, so a key granted again after its row was
    /// purged still gets a generation above every earlier one.
    /// </summary>
    public long NextGeneration()
    {
        return Interlocked.Increment(ref _lastGeneration);
    }

    public void Dispose()
    {
        Table.Dispose();
    }
}
