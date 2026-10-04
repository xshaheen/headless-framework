// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Internal;
using Headless.Reliability;
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
                ? scheduler.ScheduleAfterAsync<RequestlessJob>(
                    TimeSpan.Zero,
                    options => options.WithRetries(0),
                    AbortToken
                )
                : scheduler.ScheduleAfterAsync<RequestlessJob>(TimeSpan.Zero, AbortToken)
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
                ? scheduler.ScheduleAfterAsync<RequestlessJob>(TimeSpan.FromSeconds(2), _ => { }, AbortToken)
                : scheduler.ScheduleAfterAsync<RequestlessJob>(TimeSpan.FromSeconds(2), AbortToken);
        await negative.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await overflow.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await time.DidNotReceive().AddAsync(Arg.Any<TimeJobEntity>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task failure_policy_defaults_apply_to_ordinary_keyed_and_chain_nodes(bool fluent)
    {
        // One immediate retry, then one delayed retry of 2 seconds: stored as 2 retries waiting 0 and 2 seconds.
        var policies = new JobSchedulingPolicies(
            new JobOptions(),
            new() { [typeof(Request)] = new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed } },
            [],
            _Registry(defaultPolicy: policy =>
                policy.Immediate(1).Delayed(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5))
            )
        );
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider(), policies);
        var call = new JobOptions { RetryIntervals = [7], Description = "invocation" };
        TimeJobEntity? ordinary = null;
        time.AddAsync(Arg.Any<TimeJobEntity>(), AbortToken).Returns(info => ordinary = info.Arg<TimeJobEntity>());
        await (
            fluent
                ? scheduler.EnqueueAsync(
                    new Request(),
                    options => options.WithRetryIntervals(7).WithDescription("invocation"),
                    AbortToken
                )
                : scheduler.EnqueueAsync(new Request(), call, AbortToken)
        );
        ordinary.Should().NotBeNull();
        ordinary.Retries.Should().Be(2);
        ordinary.RetryIntervals.Should().Equal(7);
        ordinary.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        ordinary.Description.Should().Be("invocation");

        await scheduler.ScheduleKeyedAsync(new JobKey("invoice"), new Request(), DateTimeOffset.UnixEpoch, AbortToken);
        await time.Received(1)
            .ScheduleKeyedAsync(
                Arg.Any<JobKey>(),
                Arg.Is<TimeJobEntity>(job => job.Retries == 2 && job.RetryIntervals!.SequenceEqual(new[] { 0, 2 })),
                null,
                AbortToken
            );
        var chain = JobChain.Start(new Request());
        chain.Root.Then<RequestlessJob>();
        await scheduler.EnqueueAsync(chain.Build(), AbortToken);
        ordinary!.Retries.Should().Be(2);
        ordinary.RetryIntervals.Should().Equal(0, 2);
        ordinary.RetryIntervals![0] = 123;
        ordinary.Children.Single().Retries.Should().Be(2);
        ordinary.Children.Single().RetryIntervals.Should().Equal(0, 2);
    }

    [Fact]
    public async Task call_retries_replace_the_flattened_policy_and_keep_only_the_call_intervals()
    {
        var policies = new JobSchedulingPolicies(
            new JobOptions(),
            [],
            [],
            _Registry(defaultPolicy: policy => policy.Immediate(3))
        );
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider(), policies);
        TimeJobEntity? stored = null;
        time.AddAsync(Arg.Any<TimeJobEntity>(), AbortToken).Returns(info => stored = info.Arg<TimeJobEntity>());

        await scheduler.EnqueueAsync(new Request(), options => options.WithRetries(0), AbortToken);
        stored!.Retries.Should().Be(0);
        stored.RetryIntervals.Should().BeNull();

        await scheduler.EnqueueAsync(
            new Request(),
            options => options.WithRetries(1).WithRetryIntervals(4),
            AbortToken
        );
        stored.Retries.Should().Be(1);
        stored.RetryIntervals.Should().Equal(4);
    }

    [Fact]
    public async Task invalid_fluent_retry_and_node_death_settings_fail_before_persistence()
    {
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider());
        var invalidRetries = () =>
            scheduler.EnqueueAsync(new Request(), options => options.WithRetries(-1), AbortToken);
        var invalidIntervals = () =>
            scheduler.EnqueueAsync<RequestlessJob>(options => options.WithRetryIntervals(-1), AbortToken);
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
    public async Task keyed_cancellation_forwards_a_known_scope_and_rejects_an_unknown_function()
    {
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider());
        var scope = new JobKeyScope(_Typed.FunctionName, "tenant");
        var key = new JobKey("invoice");
        await scheduler.CancelKeyedAsync(scope, key, 7, AbortToken);
        await time.Received(1).CancelKeyedAsync(scope, key, 7, AbortToken);
        var unknown = () => scheduler.CancelKeyedAsync(new JobKeyScope("unknown.function"), key, 7, AbortToken);
        await unknown.Should().ThrowAsync<JobFunctionNotFoundException>();
        await time.Received(1)
            .CancelKeyedAsync(Arg.Any<JobKeyScope>(), Arg.Any<JobKey>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task recurring_facade_preserves_retry_precedence()
    {
        var policies = new JobSchedulingPolicies(
            new JobOptions { OnNodeDeath = NodeDeathPolicy.Skip },
            [],
            [],
            _Registry(
                byFunction: new(StringComparer.Ordinal)
                {
                    [_Typed.FunctionName] = policy =>
                        policy.Delayed(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)),
                    [_Requestless.FunctionName] = policy => policy.Immediate(3),
                }
            )
        );
        var (scheduler, _, cron) = _CreateScheduler(new FakeTimeProvider(), policies);
        await scheduler.ScheduleRecurringAsync(new Request(), "0 * * * * *", AbortToken);
        await cron.Received(1)
            .AddAsync(
                Arg.Is<CronJobEntity>(job =>
                    job.Retries == 2
                    && job.OnNodeDeath == NodeDeathPolicy.Skip
                    && job.RetryIntervals!.SequenceEqual(new[] { 2, 4 })
                ),
                AbortToken
            );
        await scheduler.ScheduleRecurringAsync<RequestlessJob>("0 * * * * *", AbortToken);
        await cron.Received(1)
            .AddAsync(
                Arg.Is<CronJobEntity>(job => job.Retries == 3 && job.RetryIntervals!.SequenceEqual(new[] { 0, 0, 0 })),
                AbortToken
            );
        await scheduler.ScheduleRecurringAsync(
            new Request(),
            "0 * * * * *",
            new RecurringJobOptions
            {
                Retries = 0,
                RetryIntervals = [],
                OnNodeDeath = NodeDeathPolicy.Retry,
            },
            AbortToken
        );
        await cron.Received(1)
            .AddAsync(
                Arg.Is<CronJobEntity>(job =>
                    job.Retries == 0 && job.RetryIntervals!.Length == 0 && job.OnNodeDeath == NodeDeathPolicy.Retry
                ),
                AbortToken
            );
    }

    [Fact]
    public void fluent_configuration_supports_typed_and_requestless_chaining()
    {
        var services = new ServiceCollection();
        services.AddHeadlessJobs(options =>
        {
            options
                .ConfigureDefaults(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip))
                .ConfigureJob<Request>(job => job.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed))
                .Tune(
                    _Requestless.FunctionName,
                    tune => tune.Options(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Retry))
                )
                .DefaultFailurePolicy(policy => policy.Immediate(1))
                .Should()
                .BeSameAs(options);
        });
    }

    [Fact]
    public async Task fluent_configuration_snapshots_replacement_policies_and_isolates_hosts()
    {
        JobOptionsBuilder? retained = null;
        JobsOptionsBuilder<TimeJobEntity, CronJobEntity>? captured = null;
        var callbackCount = 0;
        await using var provider = _CreateHost(options =>
        {
            captured = options;
            options.ConfigureDefaults(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Retry));
            options.ConfigureDefaults(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip));
            options.ConfigureJob<Request>(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip));
            options.ConfigureJob<Request>(job =>
            {
                ++callbackCount;
                retained = job;
                job.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed);
            });
            options.DefaultFailurePolicy(policy => policy.Immediate(9));
            options.DefaultFailurePolicy(policy => policy.Immediate(3));
            options.Tune(_Requestless.FunctionName, tune => tune.FailurePolicy(policy => policy.Immediate(9)));
            options.Tune(_Requestless.FunctionName, tune => tune.FailurePolicy(policy => policy.Immediate(2)));
            retained!.WithNodeDeathPolicy(NodeDeathPolicy.Skip);
        });
        callbackCount.Should().Be(1);
        captured!.ConfigureDefaults(job => job.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed));
        captured.ConfigureJob<Request>(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Retry));
        captured.DefaultFailurePolicy(policy => policy.Immediate(7));
        retained!.WithNodeDeathPolicy(NodeDeathPolicy.Retry);

        var scheduler = provider.GetRequiredService<IJobScheduler>();
        var persistence = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var typedId = await scheduler.EnqueueAsync(new Request(), AbortToken);
        var typed = await persistence.GetTimeJobByIdAsync(typedId, AbortToken);
        typed!.Retries.Should().Be(3);
        typed.RetryIntervals.Should().Equal(0, 0, 0);
        typed.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        var requestlessId = await scheduler.EnqueueAsync<RequestlessJob>(AbortToken);
        var requestless = await persistence.GetTimeJobByIdAsync(requestlessId, AbortToken);
        requestless!.Retries.Should().Be(2);
        requestless.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        var callId = await scheduler.EnqueueAsync(new Request(), job => job.WithRetries(0), AbortToken);
        var call = await persistence.GetTimeJobByIdAsync(callId, AbortToken);
        call!.Retries.Should().Be(0);

        await using var otherProvider = _CreateHost(options =>
            options.DefaultFailurePolicy(policy => policy.Immediate(1))
        );
        var otherId = await otherProvider.GetRequiredService<IJobScheduler>().EnqueueAsync(new Request(), AbortToken);
        var other = await otherProvider
            .GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
            .GetTimeJobByIdAsync(otherId, AbortToken);
        other!.Retries.Should().Be(1);
        other.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Theory]
    [InlineData("defaults")]
    [InlineData("request")]
    [InlineData("descriptor")]
    public async Task fluent_configuration_failures_leave_previous_policy_intact(string target)
    {
        await using var provider = _CreateHost(options =>
        {
            Action<Action<JobOptionsBuilder>> configure = target switch
            {
                "defaults" => callback => options.ConfigureDefaults(callback),
                "request" => callback => options.ConfigureJob<Request>(callback),
                _ => callback => options.Tune(_Requestless.FunctionName, tune => tune.Options(callback)),
            };
            configure(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip));
            Action<JobOptionsBuilder>[] invalid =
            [
                job => job.WithRetries(-1),
                job => job.WithRetryIntervals(-1),
                job => job.WithRetries(1),
                job => job.WithRetryIntervals(1),
                job => job.WithNodeDeathPolicy((NodeDeathPolicy)999),
                job => job.WithCorrelationId("correlation"),
                job => job.WithCausationId("causation"),
                job => job.WithDescription("invocation"),
                job => job.WithTenantId("tenant"),
                job => job.AsSystemJob(),
            ];
            foreach (var callback in invalid)
            {
                var act = () => configure(callback);
                act.Should().Throw<ArgumentException>();
            }

            var failure = new InvalidOperationException("callback failed");
            var throwing = () =>
                configure(job =>
                {
                    job.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed);
                    throw failure;
                });
            throwing.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
            var nullCallback = () => configure(null!);
            nullCallback.Should().Throw<ArgumentNullException>();
            Action nullOptions = target switch
            {
                "defaults" => () => options.ConfigureDefaults((JobOptions)null!),
                "request" => () => options.ConfigureJob<Request>((JobOptions)null!),
                _ => () => options.Tune(_Requestless.FunctionName, tune => tune.Options((JobOptions)null!)),
            };
            nullOptions.Should().Throw<ArgumentNullException>();
        });
        var scheduler = provider.GetRequiredService<IJobScheduler>();
        var id = target is "descriptor"
            ? await scheduler.EnqueueAsync<RequestlessJob>(AbortToken)
            : await scheduler.EnqueueAsync(new Request(), AbortToken);
        var stored = await provider
            .GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>()
            .GetTimeJobByIdAsync(id, AbortToken);
        stored!.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
    }

    [Theory]
    [InlineData("unknown-request")]
    [InlineData("unknown-descriptor")]
    [InlineData("duplicate-identity")]
    public async Task fluent_configuration_preserves_identity_validation_at_resolution(string target)
    {
        await using var provider = _CreateHost(options =>
        {
            switch (target)
            {
                case "unknown-request":
                    options.ConfigureJob<string>(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip));
                    break;
                case "unknown-descriptor":
                    options.Tune(
                        "tests.unknown",
                        tune => tune.Options(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip))
                    );
                    break;
                default:
                    options.ConfigureJob<Request>(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip));
                    options.Tune(
                        _Typed.FunctionName,
                        tune => tune.Options(job => job.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed))
                    );
                    break;
            }
        });
        var resolve = () => provider.GetRequiredService<IJobScheduler>();
        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void fluent_descriptor_configuration_rejects_null_before_invoking_callback()
    {
        var services = new ServiceCollection();
        services.AddHeadlessJobs(options =>
        {
            var called = false;
            var configure = () => options.Tune(null!, _ => called = true);
            configure.Should().Throw<ArgumentNullException>();
            called.Should().BeFalse();
        });
    }

    [Fact]
    public async Task each_host_freezes_policies_and_requestless_config_survives_cron_projection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        JobsOptionsBuilder<TimeJobEntity, CronJobEntity>? captured = null;
        services.AddHeadlessJobs(options =>
        {
            captured = options;
            options
                .DisableBackgroundServices()
                .ConfigureDefaults(new JobOptions { OnNodeDeath = NodeDeathPolicy.Skip });
        });
        captured!.ConfigureDefaults(new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed });
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
        stored!.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);

        var canonical = new JobFunctionDescriptor(_Requestless.FunctionName, null, "%Cron%", JobPriority.Normal, 0);
        var policies = new JobSchedulingPolicies(
            new JobOptions(),
            [],
            new(StringComparer.Ordinal)
            {
                [canonical.FunctionName] = new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed },
            },
            _Registry(
                byFunction: new(StringComparer.Ordinal) { [canonical.FunctionName] = policy => policy.Immediate(8) }
            )
        );
        var projected = policies.Resolve(
            new JobFunctionDescriptor(_Requestless.FunctionName, null, "0 * * * * *", JobPriority.Normal, 0),
            null
        );
        projected.Retries.Should().Be(8);
        projected.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        var other = new JobSchedulingPolicies(new JobOptions { OnNodeDeath = NodeDeathPolicy.Skip }, [], []);
        other.Resolve(_Typed, null).OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        other.Resolve(_Typed, null).Retries.Should().Be(0);
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
            new(StringComparer.Ordinal) { [_Requestless.FunctionName] = new JobOptions() }
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
                new JobOptions { Retries = 1 },
                new JobOptions { RetryIntervals = [1] },
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

    [Theory]
    [InlineData(-420, false)]
    [InlineData(-420, true)]
    [InlineData(330, false)]
    [InlineData(330, true)]
    public async Task explicit_offsets_survive_absolute_scheduling_and_every_chain_edge(
        int offsetMinutes,
        bool requestless
    )
    {
        var instant = new DateTimeOffset(2030, 3, 4, 5, 6, 7, TimeSpan.FromMinutes(offsetMinutes));
        var (scheduler, time, _) = _CreateScheduler(new FakeTimeProvider());
        TimeJobEntity? captured = null;
        time.AddAsync(Arg.Any<TimeJobEntity>(), AbortToken).Returns(call => captured = call.Arg<TimeJobEntity>());
        await (
            requestless
                ? scheduler.ScheduleAsync<RequestlessJob>(instant, AbortToken)
                : scheduler.ScheduleAsync(new Request(), instant, AbortToken)
        );
        captured!.ExecutionTime.Should().Be(instant.UtcDateTime);
        captured.ExecutionTime.Value.Kind.Should().Be(DateTimeKind.Utc);

        var chain = requestless ? JobChain.Start<RequestlessJob>(instant) : JobChain.Start(new Request(), instant);
        if (requestless)
        {
            chain.Root.Then<RequestlessJob>(instant);
            chain.Root.Catch<RequestlessJob>(instant);
        }
        else
        {
            chain.Root.Then(new Request(), instant);
            chain.Root.Catch(new Request(), instant);
        }

        await scheduler.EnqueueAsync(chain.Build(), AbortToken);
        captured!.Children.Should().HaveCount(2);
        foreach (var node in captured.Children.Prepend(captured))
        {
            node.ExecutionTime.Should().Be(instant.UtcDateTime);
            node.ExecutionTime.Value.Kind.Should().Be(DateTimeKind.Utc);
        }
    }

    private static JobFunctionRegistry _Registry(
        Action<FailurePolicyBuilder>? defaultPolicy = null,
        Dictionary<string, Action<FailurePolicyBuilder>>? byFunction = null
    )
    {
        return JobFunctionRegistryBuilder.Build([], [], []) with
        {
            DefaultFailurePolicy = defaultPolicy is null ? FailurePolicyDefinition.None : _Build(defaultPolicy),
            FailurePolicies = (byFunction ?? []).ToFrozenDictionary(
                pair => pair.Key,
                pair => _Build(pair.Value),
                StringComparer.Ordinal
            ),
        };

        static FailurePolicyDefinition _Build(Action<FailurePolicyBuilder> configure)
        {
            var builder = new FailurePolicyBuilder();
            configure(builder);
            return builder.Build();
        }
    }

    private static ServiceProvider _CreateHost(
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>> configure,
        string requestlessCronExpression = ""
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            if (string.IsNullOrEmpty(requestlessCronExpression))
            {
                options.AddModule<DefaultsModule>();
            }
            else
            {
                options.AddModule<CronDefaultsModule>();
            }

            configure(options);
        });
        return services.BuildServiceProvider();
    }

    private static void _Register(JobsCatalogBuilder catalog, string requestlessCronExpression)
    {
        var descriptors = new[]
        {
            _Typed,
            new JobFunctionDescriptor(
                _Requestless.FunctionName,
                null,
                requestlessCronExpression,
                _Requestless.Priority,
                _Requestless.MaxConcurrency
            ),
        };
        catalog.AddFunctions(
            descriptors.ToDictionary(
                descriptor => descriptor.FunctionName,
                descriptor => new JobFunctionRegistration
                {
                    CronExpression = descriptor.CronExpression,
                    Priority = JobPriority.Normal,
                    MaxConcurrency = 0,
                    Delegate = (_, _, _) => Task.CompletedTask,
                    JobType = descriptor.RequestType is null ? typeof(RequestlessJob) : null,
                },
                StringComparer.Ordinal
            )
        );
        catalog.AddDescriptors(descriptors.ToDictionary(descriptor => descriptor.FunctionName, StringComparer.Ordinal));
    }

    private sealed class DefaultsModule : IJobsModule
    {
        private DefaultsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
            _Register(catalog, requestlessCronExpression: "");
    }

    private sealed class CronDefaultsModule : IJobsModule
    {
        private CronDefaultsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
            _Register(catalog, requestlessCronExpression: "0 */5 * * * *");
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
            descriptorByJobType: type => type == typeof(RequestlessJob) ? _Requestless : null,
            timeProvider: clock,
            policies: policies
        );
        return (scheduler, time, cron);
    }

    private sealed record Request;

    private sealed class RequestlessJob : Headless.Jobs.IJob
    {
        public ValueTask ExecuteAsync(Headless.Jobs.JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
