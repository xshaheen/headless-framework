// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Values of the <c>reason</c> dimension on the <c>headless.lock.failed</c> and
/// <c>headless.semaphore.failed</c> counters. Exposed so consumers writing alert rules
/// (for example <c>rate(headless.lock.failed{reason="stalled"})</c>) have a compile-time
/// anchor to the tag values instead of hard-coding the strings.
/// </summary>
[PublicAPI]
public static class DistributedLockFailureReasons
{
    /// <summary>Routine not-acquired outcome: the lock/slot was held by another holder, the
    /// acquire timeout elapsed, or a transient storage error was swallowed and retried.</summary>
    public const string Contended = "contended";

    /// <summary>A non-blocking try-once acquire (<c>AcquireTimeout = TimeSpan.Zero</c>) whose single
    /// storage attempt hit the internal safety deadline — the lock-store stalled rather than the
    /// resource being contended. Surfaced even when the caller's token never fires.</summary>
    public const string Stalled = "stalled";
}
