// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Jobs;

/// <summary>
/// Adapts the Jobs change read to the dashboard's change source: the dashboard cursor is the Jobs change cursor's own
/// text form, so a cursor one node returned resumes on any other, and each changed row is read back for its
/// projection, with the read-back row's change marker as the version so the projection and version always match.
/// </summary>
/// <remarks>
/// The reader reads every Jobs table: time jobs, cron jobs, and cron occurrences. A row deleted between the change read
/// and its read-back goes out with no projection, so open views refetch it and say it no longer exists.
/// </remarks>
internal sealed class JobsChangeSource(
    string moduleId,
    IJobsDashboardBackend backend,
    TimeProvider timeProvider,
    int pageSize,
    TimeSpan overlapWindow
)
{
    private readonly string _moduleId = Argument.IsNotNullOrWhiteSpace(moduleId);
    private readonly IJobsDashboardBackend _backend = Argument.IsNotNull(backend);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);

    public async Task<string> GetStartCursorAsync(CancellationToken cancellationToken)
    {
        return (await _backend.GetChangeStartCursorAsync(cancellationToken).ConfigureAwait(false)).ToString();
    }

    /// <summary>Reads the changes after <paramref name="cursor"/>, each read back for its projection.</summary>
    /// <exception cref="ArgumentException"><paramref name="cursor"/> is not a Jobs change cursor.</exception>
    public async Task<DashboardChangeBatch> ReadChangesAsync(string cursor, CancellationToken cancellationToken)
    {
        if (!JobChangeCursor.TryParse(cursor, out var parsed))
        {
            throw new ArgumentException($"'{cursor}' is not a Jobs change cursor.", nameof(cursor));
        }

        var page = await _backend
            .ReadChangesAsync(parsed, pageSize, overlapWindow, cancellationToken)
            .ConfigureAwait(false);
        var changes = new List<DashboardChange>(page.Changes.Count);

        foreach (var change in page.Changes)
        {
            changes.Add(await _ReadBackAsync(change, cancellationToken).ConfigureAwait(false));
        }

        return new DashboardChangeBatch { Changes = changes, Cursor = page.Cursor.ToString() };
    }

    // A change read cannot tell whether a row entered or left a Needs-you state, so every read change refreshes it.
    private async Task<DashboardChange> _ReadBackAsync(JobChange change, CancellationToken cancellationToken)
    {
        var version = change.Version.ToString(CultureInfo.InvariantCulture);

        if (change.Kind == JobChangeKind.CronJob)
        {
            var cronJob = await _backend.GetCronJobAsync(change.Id, cancellationToken).ConfigureAwait(false);

            if (cronJob is not null)
            {
                var preview = _backend.Preview(
                    cronJob.Expression,
                    cronJob.TimeZoneId,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    count: 1
                );

                return JobsViews.Change(_moduleId, cronJob, preview);
            }

            return _Gone(JobsViews.CronJobKind, change.Id, version);
        }

        var run = await _backend.GetRunAsync(change.Id, cancellationToken).ConfigureAwait(false);

        return run is null
            ? _Gone(JobsViews.RunKind, change.Id, version)
            : JobsViews.Change(_moduleId, run, affectsAttention: true);
    }

    private DashboardChange _Gone(string kind, Guid id, string version) =>
        JobsViews.Removed(_moduleId, kind, id) with
        {
            Version = version,
        };
}
