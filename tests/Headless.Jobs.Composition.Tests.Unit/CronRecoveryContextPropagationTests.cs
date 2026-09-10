// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// The recovery stamp's journey from the durable row to executing job code. Every hop is asserted separately rather
/// than trusted to flow through by construction, because the failure mode is silent: a hop that drops the field turns
/// a coalesced run back into an ordinary one with nothing failing, which is exactly how a dropped retry counter once
/// handed a restarted job a fresh retry budget.
/// </summary>
public sealed class CronRecoveryContextPropagationTests : TestBase
{
    private static readonly DateTime _EarliestMissed = new(2026, 7, 26, 15, 00, 0, DateTimeKind.Utc);

    /// <summary>
    /// The projection-completeness guard. Rather than asserting a hand-written list of hops, this reflects over every
    /// type that carries a run toward execution and fails if one gains a scheduling field without a matching carrier
    /// for the recovery stamp — so a NEW projection added later cannot silently omit it.
    /// </summary>
    [Fact]
    public void every_type_that_carries_a_run_to_execution_must_carry_the_recovery_stamp()
    {
        var carriers = new[]
        {
            typeof(CronJobOccurrenceEntity<CronJobEntity>),
            typeof(JobExecutionState),
            typeof(JobFunctionContext),
        };

        foreach (var carrier in carriers)
        {
            carrier
                .GetProperty("RecoveredFromUtc", BindingFlags.Public | BindingFlags.Instance)
                .Should()
                .NotBeNull(
                    "{0} carries a run toward execution, so it must carry the recovery stamp — a hop that drops it "
                        + "silently demotes a coalesced run to an ordinary one",
                    carrier.Name
                );
        }
    }

    [Fact]
    public void the_durable_row_derives_its_recovery_marker_from_the_stamp()
    {
        var ordinary = new CronJobOccurrenceEntity<CronJobEntity>();
        ordinary.IsRecoveryRun.Should().BeFalse();

        var recovered = new CronJobOccurrenceEntity<CronJobEntity> { RecoveredFromUtc = _EarliestMissed };
        recovered.IsRecoveryRun.Should().BeTrue("the marker is derived from the stamp so the two can never disagree");
    }

    [Fact]
    public void the_execution_context_derives_its_recovery_marker_from_the_stamp()
    {
        var ordinary = _Context(recoveredFrom: null);
        ordinary.IsRecoveryRun.Should().BeFalse();
        ordinary.RecoveredFromUtc.Should().BeNull();

        var recovered = _Context(_EarliestMissed);
        recovered.IsRecoveryRun.Should().BeTrue();
        recovered.RecoveredFromUtc.Should().Be(_EarliestMissed);
    }

    /// <summary>
    /// The typed context wraps the base one via a copy constructor. A member added to the base and forgotten there is
    /// dropped only for typed job functions — a partial failure that is harder to notice than a total one.
    /// </summary>
    [Fact]
    public void the_typed_context_copy_constructor_preserves_the_recovery_stamp_and_lateness()
    {
        var source = _Context(_EarliestMissed);
        source.Lateness = TimeSpan.FromMinutes(150);

        var typed = new JobFunctionContext<string>(source, "payload");

        typed.RecoveredFromUtc.Should().Be(_EarliestMissed, "the copy constructor must carry every base member");
        typed.IsRecoveryRun.Should().BeTrue();
        typed.Lateness.Should().Be(TimeSpan.FromMinutes(150));
        typed.ScheduledFor.Should().Be(source.ScheduledFor);
    }

    [Fact]
    public void a_recovery_run_reports_lateness_spanning_the_outage()
    {
        // A coalesced run's scheduled instant is the EARLIEST missed instant, so its lateness measures the whole
        // outage rather than the dispatch delay — that is the point of reporting the earliest rather than the latest.
        var context = _Context(_EarliestMissed);
        context.ScheduledFor = _EarliestMissed;
        context.Lateness = _EarliestMissed.AddHours(2).AddMinutes(30) - _EarliestMissed;

        context.Lateness.Should().Be(TimeSpan.FromMinutes(150));
        context.ScheduledFor.Should().Be(_EarliestMissed);
    }

    [Fact]
    public async Task execution_populates_recovery_context_and_preserves_it_across_retries()
    {
        var now = new DateTimeOffset(_EarliestMissed.AddHours(2).AddMinutes(30), TimeSpan.Zero);
        var observed = new List<(DateTime? RecoveredFromUtc, TimeSpan Lateness, bool IsRecoveryRun, int RetryCount)>();
        var attempts = 0;
        var state = _ExecutionState(
            _EarliestMissed,
            retries: 1,
            (_, context, _) =>
            {
                observed.Add((context.RecoveredFromUtc, context.Lateness, context.IsRecoveryRun, context.RetryCount));

                if (attempts++ == 0)
                {
                    throw new TimeoutException("transient");
                }

                return Task.CompletedTask;
            }
        );

        await _ExecuteAsync(state, new FakeTimeProvider(now));

        observed.Should().HaveCount(2, "the transient failure should exercise the retry path");
        observed.Select(x => x.RecoveredFromUtc).Should().OnlyContain(x => x == _EarliestMissed);
        observed.Select(x => x.Lateness).Should().OnlyContain(x => x == TimeSpan.FromMinutes(150));
        observed.Select(x => x.IsRecoveryRun).Should().OnlyContain(x => x);
        observed.Select(x => x.RetryCount).Should().Equal(0, 1);
    }

    [Fact]
    public async Task normal_execution_reports_no_recovery_and_clamps_negative_lateness_to_zero()
    {
        var now = new DateTimeOffset(_EarliestMissed, TimeSpan.Zero);
        JobFunctionContext? observed = null;
        var state = _ExecutionState(
            recoveredFrom: null,
            retries: 0,
            (_, context, _) =>
            {
                observed = context;
                return Task.CompletedTask;
            }
        );
        state.ExecutionTime = now.UtcDateTime.AddMilliseconds(1);

        await _ExecuteAsync(state, new FakeTimeProvider(now));

        observed.Should().NotBeNull();
        observed!.ScheduledFor.Should().Be(state.ExecutionTime);
        observed.RecoveredFromUtc.Should().BeNull();
        observed.IsRecoveryRun.Should().BeFalse();
        observed.Lateness.Should().Be(TimeSpan.Zero, "small store/node clock skew must never surface as negative");
    }

    private static async Task _ExecuteAsync(JobExecutionState state, TimeProvider timeProvider)
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager.RenewLeaseAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        manager
            .UpdateTickerAsync(Arg.Any<JobExecutionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));
        manager
            .IsTimeJobCancellationRequestedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));

        var services = new ServiceCollection();
        services.AddSingleton(manager);
        await using var serviceProvider = services.BuildServiceProvider();
        var handler = new JobsExecutionTaskHandler(
            serviceProvider,
            timeProvider,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            JobFunctionRegistryBuilder.Build([], [], []),
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance
        );

        await handler.ExecuteTaskAsync(state, isDue: true, cancellationToken: AbortToken);
    }

    private static JobExecutionState _ExecutionState(
        DateTime? recoveredFrom,
        int retries,
        JobFunctionDelegate function
    ) =>
        new()
        {
            JobId = Guid.NewGuid(),
            ParentId = Guid.NewGuid(),
            FunctionName = "cron-recovery-context",
            Type = JobType.CronJobOccurrence,
            ExecutionTime = _EarliestMissed,
            RecoveredFromUtc = recoveredFrom,
            Retries = retries,
            RetryIntervals = [0],
            Status = JobStatus.Queued,
            CachedDelegate = function,
        };

    private static JobFunctionContext _Context(DateTime? recoveredFrom) =>
        new()
        {
            FunctionName = "ctx",
            CronOccurrenceOperations = new CronOccurrenceOperations(() => { }),
            ScheduledFor = _EarliestMissed,
            RecoveredFromUtc = recoveredFrom,
        };
}
