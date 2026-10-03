// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>Outcome of replaying a queued recovery item.</summary>
internal enum HybridCacheRecoveryReplayOutcome
{
    /// <summary>The pending operation was replayed against the recovered dependency.</summary>
    Replayed,

    /// <summary>The pending operation no longer applies (e.g. the L1 entry changed) and was dropped.</summary>
    Obsolete,
}
