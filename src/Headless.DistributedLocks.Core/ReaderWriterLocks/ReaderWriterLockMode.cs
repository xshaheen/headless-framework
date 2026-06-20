// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Discriminates read (shared) and write (exclusive) acquire paths inside
/// <see cref="DistributedReadWriteLock"/> and <see cref="DisposableReaderWriterLock"/>.
/// Not part of the public API; callers use <see cref="IDistributedReadWriteLock"/> directly.
/// </summary>
internal enum ReaderWriterLockMode
{
    /// <summary>Shared read mode: concurrent readers are allowed; writers are blocked.</summary>
    Read,

    /// <summary>Exclusive write mode: all concurrent readers and writers are blocked.</summary>
    Write,
}
