// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.Jobs;

/// <summary>
/// The Jobs sidebar's counts: one capped count of runs per <see cref="JobStatus"/>, keyed by the status name, the same
/// count the runs list shows when filtered to that status.
/// </summary>
internal sealed class JobsCounts(IJobsDashboardReader reader)
{
    private static readonly JobStatus[] _Statuses = Enum.GetValues<JobStatus>();

    private readonly IJobsDashboardReader _reader = Argument.IsNotNull(reader);

    public async Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, DashboardCount>(_Statuses.Length, StringComparer.Ordinal);

        // One status at a time, so one counts request holds at most one store connection at a time.
        foreach (var status in _Statuses)
        {
            var query = new JobsRunScope { Statuses = [status] }.ToQuery();
            var count = await _reader.CountRunsAsync(query, cancellationToken).ConfigureAwait(false);
            counts[status.ToString()] = new DashboardCount { Count = count.Count, Capped = count.Capped };
        }

        return counts;
    }
}
