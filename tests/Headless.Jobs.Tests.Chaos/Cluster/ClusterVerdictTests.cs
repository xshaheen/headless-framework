// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Cluster;

public sealed class ClusterVerdictTests : TestBase
{
    private static readonly TimeSpan _Bound = TimeSpan.FromSeconds(24);

    [Fact]
    public void should_pass_when_every_job_ran_within_its_policy_and_settled()
    {
        // given
        var retry = _Spec(NodeDeathPolicy.Retry, retries: 1);
        var skip = _Spec(NodeDeathPolicy.Skip);
        var ledger = _Ledger(
            [retry, skip],
            _Attempt(retry, AttemptEnd.LeaseLost),
            _Attempt(retry, AttemptEnd.Completed),
            _Attempt(skip, AttemptEnd.Completed)
        );

        // when
        var verdict = ClusterVerdict.Evaluate(
            ledger,
            [_Row(retry, JobStatus.Succeeded), _Row(skip, JobStatus.Succeeded)],
            [],
            _Bound
        );

        // then
        verdict.Violations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(NodeDeathPolicy.Skip)]
    [InlineData(NodeDeathPolicy.MarkFailed)]
    public void should_flag_an_at_most_once_job_when_it_started_twice(NodeDeathPolicy policy)
    {
        var job = _Spec(policy);
        var ledger = _Ledger([job], _Attempt(job, AttemptEnd.Completed), _Attempt(job, AttemptEnd.Completed));

        var verdict = ClusterVerdict.Evaluate(ledger, [_Row(job, JobStatus.Succeeded)], [], _Bound);

        verdict.Violations.Should().ContainSingle().Which.Should().Contain($"{job.Id}").And.Contain("started 2x");
        verdict.ViolatingJobs.Should().Equal(job.Id);
    }

    [Fact]
    public void should_flag_a_retry_job_when_it_started_past_its_budget()
    {
        var job = _Spec(NodeDeathPolicy.Retry, retries: 1);
        var ledger = _Ledger(
            [job],
            _Attempt(job, AttemptEnd.LeaseLost),
            _Attempt(job, AttemptEnd.MembershipLost),
            _Attempt(job, AttemptEnd.Completed)
        );

        var verdict = ClusterVerdict.Evaluate(ledger, [_Row(job, JobStatus.Succeeded)], [], _Bound);

        verdict.Violations.Should().ContainSingle().Which.Should().Contain("started 3x, at most 2");
    }

    [Fact]
    public void should_not_charge_a_retry_job_for_a_graceful_stop()
    {
        var job = _Spec(NodeDeathPolicy.Retry, retries: 1);
        var ledger = _Ledger(
            [job],
            _Attempt(job, AttemptEnd.GracefulStop),
            _Attempt(job, AttemptEnd.LeaseLost),
            _Attempt(job, AttemptEnd.Completed)
        );

        var verdict = ClusterVerdict.Evaluate(ledger, [_Row(job, JobStatus.Succeeded)], [], _Bound);

        verdict.Violations.Should().BeEmpty();
    }

    [Fact]
    public void should_flag_a_retry_job_that_never_started()
    {
        var job = _Spec(NodeDeathPolicy.Retry, retries: 2);

        var verdict = ClusterVerdict.Evaluate(_Ledger([job]), [_Row(job, JobStatus.Failed)], [], _Bound);

        verdict.Violations.Should().ContainSingle().Which.Should().Contain("never started");
    }

    [Fact]
    public void should_flag_a_lost_job_when_its_row_is_missing_or_not_terminal()
    {
        var missing = _Spec(NodeDeathPolicy.Skip);
        var stuck = _Spec(NodeDeathPolicy.Skip);

        var verdict = ClusterVerdict.Evaluate(
            _Ledger([missing, stuck]),
            [_Row(stuck, JobStatus.InProgress)],
            [],
            _Bound
        );

        verdict.Violations.Should().HaveCount(2);
        verdict.Violations.Should().Contain(v => v.Contains("have no row") && v.Contains($"{missing.Id}"));
        verdict.Violations.Should().Contain(v => v.Contains("not terminal") && v.Contains($"{stuck.Id}"));
    }

    [Fact]
    public void should_flag_an_orphan_only_when_it_outlives_the_bound()
    {
        var row = Guid.NewGuid();
        var brief = new OrphanInterval(new OrphanKey(Guid.NewGuid(), "c/a@1", 1), TimeSpan.Zero, _Bound);
        var lasting = new OrphanInterval(
            new OrphanKey(row, "c/b@1", 1),
            TimeSpan.Zero,
            _Bound + TimeSpan.FromSeconds(1)
        );

        var verdict = ClusterVerdict.Evaluate(_Ledger([]), [], [brief, lasting], _Bound);

        verdict.Violations.Should().ContainSingle().Which.Should().Contain($"orphan on row {row}");
    }

    [Theory]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.DueDone)]
    public void should_flag_a_completion_that_landed_after_its_run_lost_the_lease(JobStatus completed)
    {
        // given — the body saw its lease expired on the store clock, yet the row reads Succeeded under that lease
        var job = _Spec(NodeDeathPolicy.Skip);
        var lost = new RowObservation(
            JobStatus.InProgress,
            "c/a@1",
            4,
            RowLeaseState.Active,
            ExpiredOnStoreClock: true
        );
        var ledger = _Ledger([job], _Attempt(job, AttemptEnd.Completed, owner: "c/a@1", observation: lost));
        var row = new StoreRow(job.Id, completed, new RowLeaseSnapshot(4, RowLeaseState.Settled, null, "c/a@1", 0), 0);

        // when
        var verdict = ClusterVerdict.Evaluate(ledger, [row], [], _Bound);

        // then
        verdict.Violations.Should().ContainSingle().Which.Should().Contain("landed on a row their run had lost");
    }

    [Fact]
    public void should_flag_a_completion_that_overwrote_a_terminal_sweep()
    {
        var job = _Spec(NodeDeathPolicy.Skip);
        var swept = new RowObservation(
            JobStatus.Skipped,
            "c/a@1",
            4,
            RowLeaseState.Abandoned,
            ExpiredOnStoreClock: false
        );
        var ledger = _Ledger([job], _Attempt(job, AttemptEnd.Completed, owner: "c/a@1", observation: swept));
        var row = new StoreRow(
            job.Id,
            JobStatus.Succeeded,
            new RowLeaseSnapshot(5, RowLeaseState.Settled, null, "c/b@1", 1),
            0
        );

        var verdict = ClusterVerdict.Evaluate(ledger, [row], [], _Bound);

        verdict.Violations.Should().ContainSingle().Which.Should().Contain("landed on a row their run had lost");
    }

    [Fact]
    public void should_accept_a_refused_late_completion()
    {
        // The run lost the row and the sweep's Skipped stands: the fence did its job.
        var job = _Spec(NodeDeathPolicy.Skip);
        var lost = new RowObservation(
            JobStatus.InProgress,
            "c/a@1",
            4,
            RowLeaseState.Active,
            ExpiredOnStoreClock: true
        );
        var ledger = _Ledger([job], _Attempt(job, AttemptEnd.Completed, owner: "c/a@1", observation: lost));
        var row = new StoreRow(
            job.Id,
            JobStatus.Skipped,
            new RowLeaseSnapshot(4, RowLeaseState.Abandoned, null, "c/a@1", 0),
            0
        );

        var verdict = ClusterVerdict.Evaluate(ledger, [row], [], _Bound);

        verdict.Violations.Should().BeEmpty();
    }

    [Fact]
    public void should_count_jobs_whose_attempts_overlapped()
    {
        var job = _Spec(NodeDeathPolicy.Retry, retries: 2);
        var other = _Spec(NodeDeathPolicy.Retry, retries: 2);
        Attempt[] attempts =
        [
            _Attempt(job, AttemptEnd.Completed, start: 0, stop: 10),
            _Attempt(job, AttemptEnd.Completed, start: 5, stop: 12),
            _Attempt(other, AttemptEnd.LeaseLost, start: 0, stop: 4),
            _Attempt(other, AttemptEnd.Completed, start: 4, stop: 9),
        ];

        ClusterVerdict.ConcurrentJobs(attempts).Should().Be(1);
    }

    [Fact]
    public void should_close_an_orphan_interval_when_the_row_drops_out_of_a_sample()
    {
        var tracker = new OrphanTracker();
        var key = new OrphanKey(Guid.NewGuid(), "c/a@1", 1);

        tracker.Observe(TimeSpan.Zero, [key]);
        tracker.Observe(TimeSpan.FromSeconds(2), [key]);
        tracker.Observe(TimeSpan.FromSeconds(4), []);
        tracker.Observe(TimeSpan.FromSeconds(6), [key]);

        tracker
            .Intervals()
            .Select(interval => interval.Duration)
            .Should()
            .BeEquivalentTo([TimeSpan.FromSeconds(2), TimeSpan.Zero]);
    }

    [Fact]
    public void should_stop_recording_a_killed_incarnation()
    {
        var ledger = new JobLedger();
        var job = Guid.NewGuid();
        var attempt = ledger.Start(job, "node-0", 1, "c/a@1", 0);

        ledger.MarkKilled("node-0#1");
        attempt!.End(AttemptEnd.Completed);
        var afterKill = ledger.Start(job, "node-0", 1, "c/a@1", 0);

        var snapshot = ledger.Snapshot();
        afterKill.Should().BeNull();
        snapshot
            .Attempts.Should()
            .ContainSingle()
            .Which.Should()
            .Match<Attempt>(a => a.End == null && a.KilledAt != null);
    }

    private static JobSpec _Spec(NodeDeathPolicy policy, int retries = 0) =>
        new(Guid.NewGuid(), 0, policy, retries, TimeSpan.FromSeconds(1), IgnoresToken: false);

    private static StoreRow _Row(JobSpec spec, JobStatus status) =>
        new(spec.Id, status, new RowLeaseSnapshot(1, RowLeaseState.Settled, null, "c/a@1", 0), 0);

    private static Attempt _Attempt(
        JobSpec spec,
        AttemptEnd how,
        double start = 0,
        double stop = 1,
        string owner = "c/a@1",
        RowObservation? observation = null
    ) =>
        new(
            spec.Id,
            "node-0",
            1,
            owner,
            0,
            TimeSpan.FromSeconds(start),
            how,
            TimeSpan.FromSeconds(stop),
            observation,
            KilledAt: null
        );

    private static LedgerSnapshot _Ledger(IReadOnlyList<JobSpec> specs, params Attempt[] attempts) =>
        new(specs.ToDictionary(spec => spec.Id), attempts);
}
