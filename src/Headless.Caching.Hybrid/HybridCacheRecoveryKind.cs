// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>Kind of pending operation tracked by <see cref="HybridCacheRecoveryQueue"/>.</summary>
internal enum HybridCacheRecoveryKind
{
    SetEntry,
    Remove,
    Expire,
    PublishInvalidation,

    /// <summary>
    /// A Family-2 tag/clear/remove generation marker bump (logical RemoveByTag/Clear/Flush). Stored under a
    /// synthetic key; replay re-asserts the marker at its original timestamp (raise-only durable write) and
    /// re-broadcasts. Exempt from <see cref="HybridCacheRecoveryQueue.OnIncomingInvalidation"/> conflict drops —
    /// raise-only markers are idempotent and never resurrect stale data.
    /// </summary>
    MarkerBump,
}
