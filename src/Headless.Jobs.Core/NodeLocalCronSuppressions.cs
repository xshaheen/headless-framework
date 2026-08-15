// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Jobs;

/// <summary>
/// The cron definitions THIS HOST cannot evaluate, so it stops selecting them without recording anything the rest of
/// the fleet can see.
/// </summary>
/// <remarks>
/// Deliberately in-memory and per host. Persisting it is the defect this type exists to prevent (#830): a node whose
/// timezone database cannot resolve a definition's zone once wrote durable defer state, which suppressed that
/// definition for EVERY node — including the peers that resolve it fine. Node-local evidence stays node-local, so a
/// healthy peer keeps dispatching and the entry evaporates when this process restarts with updated tzdata.
/// <para>
/// Keyed by definition id AND schedule revision: an edit, pause, or resume bumps the revision, and the new revision may
/// name a zone this host CAN resolve. The suppression therefore lapses with the revision that earned it rather than
/// outliving the schedule it was recorded against.
/// </para>
/// </remarks>
internal sealed class NodeLocalCronSuppressions
{
    private readonly ConcurrentDictionary<Guid, long> _suppressedRevisions = new();

    /// <summary>
    /// Records that this node cannot evaluate <paramref name="cronJobId"/> at
    /// <paramref name="scheduleRevision"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this is the first suppression of this definition at this revision, so the caller can
    /// log the condition once per transition instead of once per poll — the scheduler wakes at up to ~1 kHz.
    /// </returns>
    public bool Suppress(Guid cronJobId, long scheduleRevision)
    {
        if (
            _suppressedRevisions.TryGetValue(cronJobId, out var suppressedRevision)
            && suppressedRevision == scheduleRevision
        )
        {
            return false;
        }

        _suppressedRevisions[cronJobId] = scheduleRevision;

        return true;
    }

    /// <summary>
    /// Whether this node has already given up on <paramref name="cronJobId"/> at
    /// <paramref name="scheduleRevision"/>.
    /// </summary>
    public bool IsSuppressed(Guid cronJobId, long scheduleRevision)
    {
        if (!_suppressedRevisions.TryGetValue(cronJobId, out var suppressedRevision))
        {
            return false;
        }

        if (suppressedRevision == scheduleRevision)
        {
            return true;
        }

        // The definition moved on since this node gave up on it, so the entry describes a schedule that no longer
        // exists. Removing it conditionally on the value read above means a concurrent Suppress for the NEW revision
        // is never dropped by this cleanup.
        _suppressedRevisions.TryRemove(new KeyValuePair<Guid, long>(cronJobId, suppressedRevision));

        return false;
    }
}
