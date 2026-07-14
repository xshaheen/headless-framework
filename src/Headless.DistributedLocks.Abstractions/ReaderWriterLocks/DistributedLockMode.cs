// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// The access mode requested for one resource in a reader-writer composite acquisition.
/// </summary>
/// <remarks>
/// Members may be added in future versions; treat an unrecognized value as an error rather than assuming a default.
/// </remarks>
[PublicAPI]
public enum DistributedLockMode
{
    /// <summary>
    /// No mode. This sentinel exists so that <see langword="default"/> is an <em>invalid</em> request rather than a
    /// silently-shared read lock: composite canonicalization rejects it before any provider is called. Never pass it.
    /// </summary>
    None = 0,

    /// <summary>A read (shared) lock. Concurrent readers of the same resource do not conflict.</summary>
    Read = 1,

    /// <summary>
    /// A write (exclusive) lock. Conflicts with every other reader and writer of the same resource, and subsumes
    /// <see cref="Read"/> — a resource requested in both modes within one composite collapses to <see cref="Write"/>.
    /// </summary>
    Write = 2,
}
