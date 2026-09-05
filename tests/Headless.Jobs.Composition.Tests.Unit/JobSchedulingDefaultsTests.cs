// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

[Collection<JobsHelperCollection>]
public sealed class JobSchedulingDefaultsTests : TestBase
{
    private static readonly JobFunctionDescriptor _Typed = new(
        "typed-defaults",
        typeof(Request),
        "",
        JobPriority.Normal,
        0
    );
    private static readonly JobFunctionDescriptor _Requestless = new(
        "requestless-defaults",
        null,
        "",
        JobPriority.Normal,
        0
    );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task relative_and_absolute_schedules_preserve_the_instant_using_the_injected_clock(bool fluent)
    {
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var (scheduler, time, _) = _CreateScheduler(clock);
        await (
            fluent
                ? scheduler.ScheduleAfterAsync(
                    new Request(),
                    TimeSpan.FromHours(2),
                    options => options.WithRetries(0),
                    AbortToken
                )
                : scheduler.ScheduleAfterAsync(new Request(), TimeSpan.FromHours(2), AbortToken)
        );
        await time.Received(1)
            .AddAsync(Arg.Is<TimeJobEntity>(job => job.ExecutionTime == now.AddHours(2).UtcDateTime), AbortToken);
        clock.Advance(TimeSpan.FromMinutes(30));
        await (
            fluent
                ? scheduler.ScheduleAfterAsync(
                    _Requestless,
                    TimeSpan.Zero,
                    options => options.WithRetries(0),
                    AbortToken
                )
                : scheduler.ScheduleAfterAsync(_Requestless, TimeSpan.Zero, AbortToken)
        );
        await time.Received(1)
            .AddAsync(Arg.Is<TimeJobEntity>(job => job.ExecutionTime == now.AddMinutes(30).UtcDateTime), AbortToken);
        var offset = new DateTimeOffset(2026, 9, 5, 18, 0, 0, TimeSpan.FromHours(3));
        await (
            fluent
                ? scheduler.ScheduleAsync(new Request(), offset, options => options.WithRetries(0), AbortToken)
                : scheduler.ScheduleAsync(new Request(), offset, AbortToken)
        );
        await time.Received(1)
            .AddAsync(
                Arg.Is<TimeJobEntity>(job =>
                    job.ExecutionTime == offset.UtcDateTime && job.ExecutionTime.Value.Kind == DateTimeKind.Utc
                ),
                AbortToken
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task invalid_relative_delays_fail_before_persistence(bool fluent)
    {
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider(DateTimeOffset.MaxValue.AddSeconds(-1)));
        var negative = () =>
            fluent
                ? scheduler.ScheduleAfterAsync(new Request(), TimeSpan.FromTicks(-1), _ => { }, AbortToken)
                : scheduler.ScheduleAfterAsync(new Request(), TimeSpan.FromTicks(-1), AbortToken);
        var overflow = () =>
            fluent
                ? scheduler.ScheduleAfterAsync(_Requestless, TimeSpan.FromSeconds(2), _ => { }, AbortToken)
                : scheduler.ScheduleAfterAsync(_Requestless, TimeSpan.FromSeconds(2), AbortToken);
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await overflow.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await time.DidNotReceive().AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task field_defaults_apply_to_ordinary_keyed_and_chain_nodes_and_cannot_weaken_atomic_policy(
        bool fluent
    )
    {
        var intervals = new[] { 2, 5 };
        var policies = new JobSchedulingPolicies(
            new JobOptions
            {
                Retries = 5,
                RetryIntervals = intervals,
                RequireAtomicEnlistment = true,
            },
            new() { [typeof(Request)] = new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed } },
            []
        );
        intervals[0] = 999;
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider(), policies);
        var call = new JobOptions
        {
            Retries = 0,
            Description = "invocation",
            RequireAtomicEnlistment = false,
        };
        TimeJobEntity? ordinary = null;
        time.AddAsync(Arg.Any<TimeJobEntity>(), AbortToken).Returns(info => ordinary = info.Arg<TimeJobEntity>());
        await (
            fluent
                ? scheduler.EnqueueAsync(
                    new Request(),
                    options => options.WithRetries(0).WithDescription("invocation"),
                    AbortToken
                )
                : scheduler.EnqueueAsync(new Request(), call, AbortToken)
        );
        ordinary.Should().NotBeNull();
        ordinary.Retries.Should().Be(0);
        ordinary.RetryIntervals.Should().Equal(2, 5);
        ordinary.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        ordinary.RequireAtomicEnlistment.Should().BeTrue();
        ordinary.Description.Should().Be("invocation");
        ordinary.RetryIntervals![0] = 123;

        await scheduler.ScheduleKeyedAsync(new JobKey("invoice"), new Request(), DateTimeOffset.UnixEpoch, AbortToken);
        await time.Received(1)
            .ScheduleKeyedAsync(
                Arg.Any<JobKey>(),
                Arg.Is<TimeJobEntity>(job =>
                    job.Retries == 5 && job.RequireAtomicEnlistment && job.RetryIntervals![0] == 2
                ),
                null,
                AbortToken
            );
        var chain = JobChain.Start(new Request());
        chain.Root.Then(_Requestless);
        await scheduler.EnqueueAsync(chain.Build(), AbortToken);
        ordinary!.RequireAtomicEnlistment.Should().BeTrue();
        ordinary.Children.Single().RequireAtomicEnlistment.Should().BeTrue();
    }

    [Fact]
    public async Task invalid_fluent_retry_and_node_death_settings_fail_before_persistence()
    {
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider());
        var invalidRetries = () =>
            scheduler.EnqueueAsync(new Request(), options => options.WithRetries(-1), AbortToken);
        var invalidIntervals = () =>
            scheduler.EnqueueAsync(_Requestless, options => options.WithRetryIntervals(-1), AbortToken);
        var invalidPolicy = () =>
            scheduler.EnqueueAsync(
                new Request(),
                options => options.WithNodeDeathPolicy((NodeDeathPolicy)999),
                AbortToken
            );
        await invalidRetries.Should().ThrowAsync<ArgumentException>();
        await invalidIntervals.Should().ThrowAsync<ArgumentException>();
        await invalidPolicy.Should().ThrowAsync<ArgumentException>();
        await time.DidNotReceive().AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task keyed_cancellation_uses_the_scope_function_policy_even_when_call_says_false()
    {
        var policies = new JobSchedulingPolicies(
            new JobOptions(),
            new() { [typeof(Request)] = new JobOptions { RequireAtomicEnlistment = true } },
            []
        );
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider(), policies);
        var scope = new JobKeyScope(_Typed.FunctionName, "tenant");
        var key = new JobKey("invoice");
        await scheduler.CancelKeyedAsync(scope, key, 7, false, AbortToken);
        await time.Received(1).CancelKeyedAsync(scope, key, 7, true, AbortToken);
    }

    [Fact]
    public async Task recurring_facade_inherits_retries_but_rejects_required_atomic_policy()
    {
        var policies = new JobSchedulingPolicies(
            new JobOptions { Retries = 4, OnNodeDeath = NodeDeathPolicy.Skip },
            [],
            []
        );
        var (scheduler, _, cron) = _CreateScheduler(new FakeTimeProvider(), policies);
        await scheduler.ScheduleRecurringAsync(new Request(), "0 * * * * *", AbortToken);
        await cron.Received(1)
            .AddAsync(
                Arg.Is<CronJobEntity>(job => job.Retries == 4 && job.OnNodeDeath == NodeDeathPolicy.Skip),
                AbortToken
            );
        var (atomicScheduler, _, atomicCron) = _CreateScheduler(
            new FakeTimeProvider(),
            new JobSchedulingPolicies(new JobOptions { RequireAtomicEnlistment = true }, [], [])
        );
        var schedule = () => atomicScheduler.ScheduleRecurringAsync(_Requestless, "0 * * * * *", AbortToken);
        await schedule.Should().ThrowAsync<NotSupportedException>();
        await atomicCron.DidNotReceive().AddAsync(Arg.Any<CronJobEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task each_host_freezes_policies_and_requestless_config_survives_cron_projection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        JobsOptionsBuilder<TimeJobEntity, CronJobEntity>? captured = null;
        var intervals = new[] { 2 };
        services.AddHeadlessJobs(options =>
        {
            captured = options;
            options
                .DisableBackgroundServices()
                .ConfigureDefaults(new JobOptions { Retries = 3, RetryIntervals = intervals });
        });
        captured!.ConfigureDefaults(new JobOptions { Retries = 99 });
        intervals[0] = 99;
        services.AddSingleton(
            JobFunctionRegistryBuilder.Build(
                [
                    new KeyValuePair<string, JobFunctionRegistration>(
                        _Typed.FunctionName,
                        new()
                        {
                            CronExpression = "",
                            Priority = JobPriority.Normal,
                            MaxConcurrency = 0,
                            Delegate = (_, _, _) => Task.CompletedTask,
                        }
                    ),
                ],
                [],
                [new KeyValuePair<string, JobFunctionDescriptor>(_Typed.FunctionName, _Typed)]
            )
        );
        await using var provider = services.BuildServiceProvider();
        var id = await provider.GetRequiredService<IJobScheduler>().EnqueueAsync(new Request(), AbortToken);
        var persistence = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var stored = await persistence.GetTimeJobByIdAsync(id, AbortToken);
        stored!.Retries.Should().Be(3);
        stored.RetryIntervals.Should().Equal(2);

        var canonical = new JobFunctionDescriptor(_Requestless.FunctionName, null, "%Cron%", JobPriority.Normal, 0);
        var policies = new JobSchedulingPolicies(
            new JobOptions(),
            [],
            new() { [canonical] = new JobOptions { Retries = 8 } }
        );
        policies
            .Resolve(
                new JobFunctionDescriptor(_Requestless.FunctionName, null, "0 * * * * *", JobPriority.Normal, 0),
                null
            )
            .Retries.Should()
            .Be(8);
        var other = new JobSchedulingPolicies(new JobOptions { Retries = 1 }, [], []);
        other.Resolve(_Typed, null).Retries.Should().Be(1);
    }

    [Fact]
    public void configuration_rejects_unknown_identity_invalid_retry_values_and_invocation_metadata()
    {
        var invalidRequest = new JobSchedulingPolicies(
            new JobOptions(),
            new() { [typeof(Request)] = new JobOptions() },
            []
        );
        var invalidDescriptor = new JobSchedulingPolicies(
            new JobOptions(),
            [],
            new() { [_Requestless] = new JobOptions() }
        );
        var registry = JobFunctionRegistryBuilder.Build([], [], []);
        var validateRequest = () => invalidRequest.Validate(registry);
        var validateDescriptor = () => invalidDescriptor.Validate(registry);
        validateRequest.Should().Throw<InvalidOperationException>();
        validateDescriptor.Should().Throw<InvalidOperationException>();
        foreach (
            var options in new[]
            {
                new JobOptions { Retries = -1 },
                new JobOptions { RetryIntervals = [-1] },
                new JobOptions { OnNodeDeath = (NodeDeathPolicy)999 },
                new JobOptions { TenantId = "tenant" },
                new JobOptions { CorrelationId = "correlation" },
                new JobOptions { CausationId = "cause" },
                new JobOptions { IsSystemJob = true },
                new JobOptions { Description = "invocation" },
            }
        )
        {
            var configure = () => JobSchedulingPolicies.Snapshot(options);
            configure.Should().Throw<ArgumentException>();
        }
    }

    private static (
        IJobScheduler Scheduler,
        ITimeJobManager<TimeJobEntity> Time,
        ICronJobManager<CronJobEntity> Cron
    ) _CreateScheduler(TimeProvider clock, JobSchedulingPolicies? policies = null)
    {
        var time = Substitute.For<ITimeJobManager<TimeJobEntity>>();
        var cron = Substitute.For<ICronJobManager<CronJobEntity>>();
        time.AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<TimeJobEntity>());
        cron.AddAsync(Arg.Any<CronJobEntity>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CronJobEntity>());
        var scheduler = new JobScheduler<TimeJobEntity, CronJobEntity>(
            time,
            cron,
            type => type == typeof(Request) ? _Typed : null,
            name =>
                string.Equals(name, _Typed.FunctionName, StringComparison.Ordinal) ? _Typed
                : string.Equals(name, _Requestless.FunctionName, StringComparison.Ordinal) ? _Requestless
                : null,
            Substitute.For<IInternalJobManager>(),
            Substitute.For<IJobsHostScheduler>(),
            timeProvider: clock,
            policies: policies
        );
        return (scheduler, time, cron);
    }

    private sealed record Request;
}
