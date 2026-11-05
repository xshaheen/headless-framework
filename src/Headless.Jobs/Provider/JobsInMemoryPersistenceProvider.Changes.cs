// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs.Provider;

// The change marker: every write stamps its row from one counter, so markers are unique across every table and only
// ever move forward, and change reads range over them.
internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly Lock _changeMarkerLock = new();
    private DateTime _lastChangeMarker = DateTime.MinValue;

    /// <summary>
    /// The next change marker: the store clock, or one tick past the previous marker when the clock has not moved
    /// past it, so two writes in one clock tick, or after the clock stepped back, still get increasing markers.
    /// </summary>
    private DateTime _NextChangeMarker()
    {
        lock (_changeMarkerLock)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            _lastChangeMarker = now > _lastChangeMarker ? now : _lastChangeMarker.AddTicks(1);

            return _lastChangeMarker;
        }
    }

    public Task<DateTime?> GetLatestChangeMarkerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTime? latest = null;

        foreach (var marker in _AllChangeMarkers())
        {
            if (latest is null || marker > latest)
            {
                latest = marker;
            }
        }

        return Task.FromResult(latest);
    }

    public Task<IReadOnlyList<JobChange>> ReadChangedRowsAsync(
        JobChangeKind kind,
        DateTime after,
        DateTime through,
        JobChangePosition? resumeAfter,
        int maxCount,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(maxCount);
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<(Guid Id, DateTime ChangedAt)> rows = kind switch
        {
            JobChangeKind.TimeJob => _timeJobs.Values.Select(static x => (x.Id, x.ChangedAt)),
            JobChangeKind.CronJob => _cronJobs.Values.Select(static x => (x.Id, x.ChangedAt)),
            JobChangeKind.CronJobOccurrence => _cronOccurrences.Values.Select(static x => (x.Id, x.ChangedAt)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown change kind."),
        };

        IReadOnlyList<JobChange> page =
        [
            .. rows.Where(row =>
                    row.ChangedAt > after
                    && row.ChangedAt <= through
                    && (
                        resumeAfter is not { } position
                        || row.ChangedAt > position.ChangedAt
                        || (row.ChangedAt == position.ChangedAt && row.Id.CompareTo(position.Id) > 0)
                    )
                )
                .OrderBy(static row => row.ChangedAt)
                .ThenBy(static row => row.Id)
                .Take(maxCount)
                .Select(row => new JobChange
                {
                    Kind = kind,
                    Id = row.Id,
                    ChangedAt = row.ChangedAt,
                }),
        ];

        return Task.FromResult(page);
    }

    private IEnumerable<DateTime> _AllChangeMarkers()
    {
        foreach (var job in _timeJobs.Values)
        {
            yield return job.ChangedAt;
        }

        foreach (var cronJob in _cronJobs.Values)
        {
            yield return cronJob.ChangedAt;
        }

        foreach (var occurrence in _cronOccurrences.Values)
        {
            yield return occurrence.ChangedAt;
        }
    }
}
