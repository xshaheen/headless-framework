// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Jobs.Entities;

namespace Headless.Jobs.Provider;

internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // Tuple string equality is ordinal and keeps null tenants distinct from empty tenants.
    // Every change of identity and every indexed lookup holds _keyedOperations, including publication and rollback.
    private readonly Dictionary<(string? TenantId, string Function, string BusinessKey), Guid> _currentTimeJobIds = [];

    private bool _TryAddTimeJob(Guid id, TTimeJob job)
    {
        lock (_keyedOperations)
        {
            if (_timeJobs.ContainsKey(id))
            {
                return false;
            }

            var key = _CurrentTimeJobKey(job);
            _RejectDuplicateCurrentTimeJob(key, id);
            if (!_timeJobs.TryAdd(id, job))
            {
                return false;
            }

            if (key is { } identity)
            {
                _currentTimeJobIds.Add(identity, id);
            }

            return true;
        }
    }

    private bool _TryUpdateTimeJob(Guid id, TTimeJob job, TTimeJob expected)
    {
        var previousKey = _CurrentTimeJobKey(expected);
        var key = _CurrentTimeJobKey(job);
        if (previousKey == key)
        {
            // Lifecycle writes retain the indexed ID. Keep claims concurrent and preserve the exact-instance CAS.
            return _timeJobs.TryUpdate(id, job, expected);
        }

        lock (_keyedOperations)
        {
            if (!_timeJobs.TryGetValue(id, out var current) || !ReferenceEquals(current, expected))
            {
                return false;
            }

            _RejectDuplicateCurrentTimeJob(key, id);
            if (!_timeJobs.TryUpdate(id, job, expected))
            {
                return false;
            }

            if (previousKey is { } previousIdentity)
            {
                _currentTimeJobIds.Remove(previousIdentity);
            }

            if (key is { } identity)
            {
                _currentTimeJobIds.Add(identity, id);
            }

            return true;
        }
    }

    private bool _TryRemoveTimeJob(Guid id, [NotNullWhen(true)] out TTimeJob? removed)
    {
        lock (_keyedOperations)
        {
            if (!_timeJobs.TryRemove(id, out removed))
            {
                return false;
            }

            if (_CurrentTimeJobKey(removed) is { } key)
            {
                _currentTimeJobIds.Remove(key);
            }

            return true;
        }
    }

    private bool _TryRemoveTimeJob(KeyValuePair<Guid, TTimeJob> expected)
    {
        lock (_keyedOperations)
        {
            if (!_timeJobs.TryRemove(expected))
            {
                return false;
            }

            if (_CurrentTimeJobKey(expected.Value) is { } key)
            {
                _currentTimeJobIds.Remove(key);
            }

            return true;
        }
    }

    private void _RejectDuplicateCurrentTimeJob((string? TenantId, string Function, string BusinessKey)? key, Guid id)
    {
        if (key is { } identity && _currentTimeJobIds.TryGetValue(identity, out var currentId) && currentId != id)
        {
            throw new InvalidOperationException("A current generation already exists for this scoped business key.");
        }
    }

    private static (string? TenantId, string Function, string BusinessKey)? _CurrentTimeJobKey(TTimeJob job) =>
        job.IsCurrentGeneration == true && job.BusinessKey is { } key ? (job.TenantId, job.Function, key) : null;
}
