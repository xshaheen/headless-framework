// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// One entry in a reader-writer composite acquisition: a resource and the mode it is wanted in.
/// </summary>
/// <param name="Resource">The resource to lock. Must be non-null and non-whitespace.</param>
/// <param name="Mode">
/// The access mode. Must be <see cref="DistributedLockMode.Read"/> or <see cref="DistributedLockMode.Write"/>;
/// <see cref="DistributedLockMode.None"/> is rejected before any provider call.
/// </param>
/// <remarks>
/// A set may mix modes freely. Canonicalization collapses a resource requested as both
/// <see cref="DistributedLockMode.Read"/> and <see cref="DistributedLockMode.Write"/> into a single
/// <see cref="DistributedLockMode.Write"/> entry, so every resource is acquired exactly once — which is what lets the
/// whole set be ordered by resource name alone and makes two composites over overlapping names unable to deadlock.
/// </remarks>
[PublicAPI]
public record DistributedReadWriteLockRequest(string Resource, DistributedLockMode Mode);
