// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Headless.Fencing;

namespace Headless.Jobs.Provider;

internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly JobsClusterConcurrency _clusterConcurrency;

    // Commit membership and claims under the same lock so two claims cannot both take the last slot. Keyed
    // operations and cron definition locks come first. A time claim retains its keyed identity and skips that lock.
    private readonly Lock _clusterSlotsLock = new();

    // Index IDs, leaving the row authoritative for function and expiry. A renewal changes the deadline
    // without moving the ID. An expired lease stops counting even before a sweep updates the row.
    private readonly SlotCandidates _timeJobSlotCandidates = new();
    private readonly SlotCandidates _cronOccurrenceSlotCandidates = new();

    private bool _TryClaimWithinClusterLimit(string function, DateTimeOffset now, Func<bool> claim)
    {
        if (!_clusterConcurrency.IsLimited(function))
        {
            return claim();
        }

        lock (_clusterSlotsLock)
        {
            return _HasFreeClusterSlot(function, now) && claim();
        }
    }

    // A due row whose function has no slot would keep the scheduler's wake at now until the fallback sweep takes it.
    private HashSet<string> _SaturatedClusterFunctions(DateTimeOffset now)
    {
        var saturated = new HashSet<string>(StringComparer.Ordinal);
        if (!_clusterConcurrency.HasLimits)
        {
            return saturated;
        }

        lock (_clusterSlotsLock)
        {
            foreach (var function in _clusterConcurrency.LimitedFunctions)
            {
                if (!_HasFreeClusterSlot(function, now))
                {
                    saturated.Add(function);
                }
            }
        }

        return saturated;
    }

    private bool _HasFreeClusterSlot(string function, DateTimeOffset now)
    {
        var remaining = _clusterConcurrency.LimitOf(function);
        foreach (var id in _timeJobSlotCandidates)
        {
            if (
                _timeJobs.TryGetValue(id, out var row)
                && string.Equals(row.Function, function, StringComparison.Ordinal)
                && _IsLeaseLive(row.Lease, now)
                && --remaining == 0
            )
            {
                return false;
            }
        }

        foreach (var id in _cronOccurrenceSlotCandidates)
        {
            if (
                _cronOccurrences.TryGetValue(id, out var row)
                && string.Equals(row.Function, function, StringComparison.Ordinal)
                && _IsLeaseLive(row.Lease, now)
                && --remaining == 0
            )
            {
                return false;
            }
        }

        return remaining > 0;
    }

    private bool _IsSlotCandidate(TTimeJob row) =>
        _clusterConcurrency.HasLimits && _HasActiveSlotLease(row.Status, row.Lease.State);

    private bool _IsSlotCandidate(CronJobOccurrenceEntity<TCronJob> row) =>
        _clusterConcurrency.HasLimits && _HasActiveSlotLease(row.Status, row.Lease.State);

    private static bool _HasActiveSlotLease(JobStatus status, RowLeaseState state) =>
        status is JobStatus.Queued or JobStatus.InProgress && state == RowLeaseState.Active;

    private bool _TryAddSlotRow<TRow>(
        ConcurrentDictionary<Guid, TRow> rows,
        SlotCandidates candidates,
        Guid id,
        TRow row,
        bool isCandidate
    )
    {
        if (!isCandidate)
        {
            return rows.TryAdd(id, row);
        }

        lock (_clusterSlotsLock)
        {
            if (!rows.TryAdd(id, row))
            {
                return false;
            }

            candidates.Add(id);
            return true;
        }
    }

    private bool _TryUpdateSlotRow<TRow>(
        ConcurrentDictionary<Guid, TRow> rows,
        SlotCandidates candidates,
        Guid id,
        TRow row,
        TRow expected,
        bool wasCandidate,
        bool isCandidate
    )
    {
        if (wasCandidate == isCandidate)
        {
            return rows.TryUpdate(id, row, expected);
        }

        lock (_clusterSlotsLock)
        {
            if (!rows.TryUpdate(id, row, expected))
            {
                return false;
            }

            _SetSlotMembership(candidates, id, isCandidate);
            return true;
        }
    }

    private bool _TryRemoveSlotRow<TRow>(
        ConcurrentDictionary<Guid, TRow> rows,
        SlotCandidates candidates,
        Guid id,
        [MaybeNullWhen(false)] out TRow removed
    )
    {
        if (!_clusterConcurrency.HasLimits)
        {
            return rows.TryRemove(id, out removed);
        }

        lock (_clusterSlotsLock)
        {
            if (!rows.TryRemove(id, out removed))
            {
                return false;
            }

            candidates.Remove(id);
            return true;
        }
    }

    private bool _TryRemoveSlotRow<TRow>(
        ConcurrentDictionary<Guid, TRow> rows,
        SlotCandidates candidates,
        KeyValuePair<Guid, TRow> expected,
        bool isCandidate
    )
    {
        if (!isCandidate)
        {
            return rows.TryRemove(expected);
        }

        lock (_clusterSlotsLock)
        {
            if (!rows.TryRemove(expected))
            {
                return false;
            }

            candidates.Remove(expected.Key);
            return true;
        }
    }

    private static void _SetSlotMembership(SlotCandidates candidates, Guid id, bool isCandidate)
    {
        if (isCandidate)
        {
            candidates.Add(id);
        }
        else
        {
            candidates.Remove(id);
        }
    }

    private bool _TryAddCronOccurrence(Guid id, CronJobOccurrenceEntity<TCronJob> row) =>
        _TryAddSlotRow(_cronOccurrences, _cronOccurrenceSlotCandidates, id, row, _IsSlotCandidate(row));

    private bool _TryUpdateCronOccurrence(
        Guid id,
        CronJobOccurrenceEntity<TCronJob> row,
        CronJobOccurrenceEntity<TCronJob> expected
    ) =>
        _TryUpdateSlotRow(
            _cronOccurrences,
            _cronOccurrenceSlotCandidates,
            id,
            row,
            expected,
            _IsSlotCandidate(expected),
            _IsSlotCandidate(row)
        );

    private void _SetCronOccurrence(Guid id, CronJobOccurrenceEntity<TCronJob> row)
    {
        if (!_clusterConcurrency.HasLimits)
        {
            _cronOccurrences[id] = row;
            return;
        }

        lock (_clusterSlotsLock)
        {
            _cronOccurrences[id] = row;
            _SetSlotMembership(_cronOccurrenceSlotCandidates, id, _IsSlotCandidate(row));
        }
    }

    // Compact the IDs on removal so a large publication batch or a past burst of leases cannot leave holes
    // that every later slot check has to traverse. Order is irrelevant, so removing an ID moves only the last one.
    private sealed class SlotCandidates
    {
        private readonly Dictionary<Guid, int> _positions = [];
        private readonly List<Guid> _ids = [];

        public void Add(Guid id)
        {
            if (_positions.TryAdd(id, _ids.Count))
            {
                _ids.Add(id);
            }
        }

        public void Remove(Guid id)
        {
            if (!_positions.Remove(id, out var position))
            {
                return;
            }

            var lastPosition = _ids.Count - 1;
            if (position != lastPosition)
            {
                var last = _ids[lastPosition];
                _ids[position] = last;
                _positions[last] = position;
            }

            _ids.RemoveAt(lastPosition);
        }

        public List<Guid>.Enumerator GetEnumerator() => _ids.GetEnumerator();
    }
}
