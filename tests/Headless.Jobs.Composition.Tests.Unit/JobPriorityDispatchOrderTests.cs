using Headless.Jobs.Enums;
using Headless.Jobs.JobsThreadPool;

namespace Tests;

public sealed class JobPriorityDispatchOrderTests
{
    [Fact]
    public void dispatch_rank_orders_high_before_normal_before_low_before_long_running()
    {
        JobPriority.High.DispatchRank().Should().BeLessThan(JobPriority.Normal.DispatchRank());
        JobPriority.Normal.DispatchRank().Should().BeLessThan(JobPriority.Low.DispatchRank());
        JobPriority.Low.DispatchRank().Should().BeLessThan(JobPriority.LongRunning.DispatchRank());
    }

    [Fact]
    public void ordering_by_dispatch_rank_does_not_follow_raw_enum_values()
    {
        // Normal = 0 and High = 1 are wire-stable contract values; sorting by the raw enum would dispatch
        // Normal batches before High and fill every worker slot first. This pins the rank as the sort key.
        JobPriority[] mixed = [JobPriority.Normal, JobPriority.LongRunning, JobPriority.High, JobPriority.Low];

        var ordered = mixed.OrderBy(x => x.DispatchRank()).ToArray();

        ordered.Should().Equal(JobPriority.High, JobPriority.Normal, JobPriority.Low, JobPriority.LongRunning);
    }

    [Fact]
    public void ordering_by_dispatch_rank_is_stable_within_equal_priorities()
    {
        // OrderBy is a stable sort, so equal-priority work keeps its store order (oldest-first fairness).
        (JobPriority Priority, int StoreOrder)[] batch =
        [
            (JobPriority.Normal, 1),
            (JobPriority.High, 2),
            (JobPriority.Normal, 3),
            (JobPriority.High, 4),
        ];

        var ordered = batch.OrderBy(x => x.Priority.DispatchRank()).ToArray();

        ordered
            .Should()
            .Equal((JobPriority.High, 2), (JobPriority.High, 4), (JobPriority.Normal, 1), (JobPriority.Normal, 3));
    }
}
