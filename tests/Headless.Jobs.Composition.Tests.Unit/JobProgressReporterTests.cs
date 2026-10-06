// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Threading.Channels;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class JobProgressReporterTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task should_write_the_first_report_at_once_and_coalesce_the_rest_into_one_write_per_interval()
    {
        var (reporter, time, writes, _) = _Create();

        reporter.Report(new JobProgress(1, "first"));
        (await _NextWriteAsync(writes)).Should().Be(new JobProgress(1, "first"));

        for (var percent = 2; percent <= 100; percent++)
        {
            reporter.Report(new JobProgress(percent, $"item {percent}"));
        }

        (await _AdvanceUntilWriteAsync(time, writes)).Should().Be(new JobProgress(100, "item 100"));

        (await reporter.StopAsync()).Should().BeNull("every report reached the store");
        writes.Reader.TryRead(out _).Should().BeFalse("100 reports cost two writes, not one per call");
    }

    [Fact]
    public async Task should_hand_back_the_report_still_waiting_for_its_interval_when_stopped()
    {
        var (reporter, _, writes, _) = _Create();
        reporter.Report(new JobProgress(10));
        await _NextWriteAsync(writes);

        reporter.Report(new JobProgress(55, "almost"));
        reporter.Report(new JobProgress(60, "latest"));

        (await reporter.StopAsync()).Should().Be(new JobProgress(60, "latest"));
        writes.Reader.TryRead(out _).Should().BeFalse("the throttled value goes to the terminal write instead");
    }

    [Fact]
    public async Task should_return_nothing_when_the_job_never_reported()
    {
        var (reporter, _, _, manager) = _Create();

        (await reporter.StopAsync()).Should().BeNull();

        await manager
            .DidNotReceive()
            .UpdateProgressAsync(Arg.Any<JobExecutionState>(), Arg.Any<JobProgress>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_ignore_reports_after_stop()
    {
        var (reporter, _, _, manager) = _Create();
        await reporter.StopAsync();

        reporter.Report(new JobProgress(99));

        (await reporter.StopAsync()).Should().BeNull();
        await manager
            .DidNotReceive()
            .UpdateProgressAsync(Arg.Any<JobExecutionState>(), Arg.Any<JobProgress>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_retry_the_latest_value_after_a_failed_write_without_failing()
    {
        var failFirst = true;
        var (reporter, time, writes, _) = _Create(progress =>
        {
            if (failFirst)
            {
                failFirst = false;
                throw new InvalidOperationException("store unreachable");
            }

            return true;
        });

        reporter.Report(new JobProgress(20, "lost write"));
        (await _NextWriteAsync(writes)).Should().Be(new JobProgress(20, "lost write"));
        reporter.Report(new JobProgress(30, "newer"));

        (await _AdvanceUntilWriteAsync(time, writes)).Should().Be(new JobProgress(30, "newer"));
        (await reporter.StopAsync()).Should().BeNull();
    }

    [Fact]
    public async Task should_drop_a_report_the_ownership_fence_rejected()
    {
        var (reporter, _, writes, _) = _Create(_ => false);

        reporter.Report(new JobProgress(70));
        await _NextWriteAsync(writes);

        (await reporter.StopAsync())
            .Should()
            .BeNull("a fenced row belongs to another owner, so the terminal write must not carry the value either");
    }

    [Fact]
    public async Task should_stop_writing_once_the_execution_lost_its_lease()
    {
        var owns = true;
        var (reporter, time, writes, manager) = _Create(ownsExecution: () => Volatile.Read(ref owns));
        reporter.Report(new JobProgress(10));
        await _NextWriteAsync(writes);

        Volatile.Write(ref owns, false);
        reporter.Report(new JobProgress(90, "from a zombie handler"));
        for (var tick = 0; tick < 5; tick++)
        {
            time.Advance(_Interval);
            await Task.Delay(10, AbortToken);
        }

        await reporter.StopAsync();
        writes
            .Reader.TryRead(out _)
            .Should()
            .BeFalse("a run that lost its lease must not overwrite the next run's progress");
        await manager
            .Received(1)
            .UpdateProgressAsync(Arg.Any<JobExecutionState>(), Arg.Any<JobProgress>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void should_reject_a_percent_outside_zero_to_one_hundred(double percent)
    {
        var context = new JobContext { FunctionName = "fn" };

        var act = () => context.ReportProgress(percent);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_reject_a_message_longer_than_the_column()
    {
        var context = new JobContext { FunctionName = "fn" };

        var act = () => context.ReportProgress(5, new string('x', JobProgress.MessageMaxLength + 1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_validate_and_record_nothing_on_a_context_the_scheduler_did_not_create()
    {
        var context = new JobContext { FunctionName = "fn" };

        var act = () => context.ReportProgress(100, new string('x', JobProgress.MessageMaxLength));

        act.Should().NotThrow();
    }

    [Fact]
    public void should_carry_the_progress_sink_into_a_typed_context()
    {
        var sink = Substitute.For<IJobProgressSink>();
        var context = new JobContext { FunctionName = "fn", ProgressSink = sink };
        var typed = new JobContext<string>(context, "request");

        typed.ReportProgress(42, "typed");

        sink.Received(1).Report(new JobProgress(42, "typed"));
    }

    private static (
        JobProgressReporter Reporter,
        FakeTimeProvider Time,
        Channel<JobProgress> Writes,
        IInternalJobManager Manager
    ) _Create(Func<JobProgress, bool>? write = null, Func<bool>? ownsExecution = null)
    {
        var time = new FakeTimeProvider();
        var writes = Channel.CreateUnbounded<JobProgress>();
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .UpdateProgressAsync(Arg.Any<JobExecutionState>(), Arg.Any<JobProgress>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.Arg<JobProgress>();
                writes.Writer.TryWrite(progress);
                return Task.FromResult(write?.Invoke(progress) ?? true);
            });

        var context = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            FunctionName = "fn",
            Type = JobType.TimeJob,
        };
        var reporter = new JobProgressReporter(
            context,
            manager,
            time,
            _Interval,
            ownsExecution ?? (() => true),
            NullLogger.Instance
        );
        return (reporter, time, writes, manager);
    }

    private static async Task<JobProgress> _NextWriteAsync(Channel<JobProgress> writes)
    {
        return await writes.Reader.ReadAsync(AbortToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    // The writer registers its interval wait only after the previous write returns, on a pool thread, so the test
    // keeps advancing fake time until that wait has been armed and fired. Extra intervals are harmless: with nothing
    // pending the writer just waits for the next report.
    private static async Task<JobProgress> _AdvanceUntilWriteAsync(FakeTimeProvider time, Channel<JobProgress> writes)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            if (writes.Reader.TryRead(out var progress))
            {
                return progress;
            }

            waited
                .Elapsed.Should()
                .BeLessThan(TimeSpan.FromSeconds(10), "the throttled write should follow the interval");
            time.Advance(_Interval);
            await Task.Delay(10, AbortToken);
        }
    }
}
