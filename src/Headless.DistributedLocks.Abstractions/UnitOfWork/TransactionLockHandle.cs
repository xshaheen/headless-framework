// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.DistributedLocks;

/// <summary>
/// A transaction-scoped lock held by a unit of work's transaction, returned by <c>unit.TransactionLocks</c>.
/// </summary>
/// <remarks>
/// There is nothing to release: the engine drops the lock when the transaction commits or rolls back, so the
/// handle carries identity for logging and assertions and nothing else. It is a record so two acquisitions of one
/// resource inside one unit compare equal.
/// </remarks>
/// <param name="Resource">The logical resource name the caller asked for, before provider encoding.</param>
[PublicAPI]
public sealed record TransactionLockHandle(string Resource);
