// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Reliability;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// A job's failure policy is resolved once per identity (tuning, then declaration, then host default, then no
/// retries, with configuration adjusting the numbers last) and flattened into the retry snapshot a scheduling call
/// stores, unless the call supplies its own retry count.
/// </summary>
public sealed class JobFailurePolicyResolutionTests : TestBase
{
    private const string _Declared = "policy.declared";
    private const string _Undeclared = "policy.undeclared";
    private const string _Nightly = "policy.nightly";

    [Fact]
    public async Task should_store_retries_flattened_from_the_declared_policy_when_the_call_supplies_none()
    {
        // given
        await using var provider = _Provider();
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var id = await scheduler.EnqueueAsync<DeclaredJob>(AbortToken);

        // then: 2 immediate retries wait nothing, then 3 delayed retries double from 30 seconds.
        var stored = await _TimeJobAsync(provider, id);
        stored.Retries.Should().Be(5);
        stored.RetryIntervals.Should().Equal(0, 0, 30, 60, 120);
    }

    [Fact]
    public async Task should_store_the_flattened_policy_on_a_recurring_definition()
    {
        // given
        await using var provider = _Provider();
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var id = await scheduler.ScheduleRecurringAsync<DeclaredJob>("0 0 * * * *", AbortToken);

        // then
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var definition = (await store.GetCronJobsAsync(predicate: null, AbortToken)).Single(x => x.Id == id);
        definition.Retries.Should().Be(5);
        definition.RetryIntervals.Should().Equal(0, 0, 30, 60, 120);
    }

    [Fact]
    public async Task should_let_the_call_override_the_count_while_the_job_keeps_its_declared_policy()
    {
        // given
        await using var provider = _Provider();
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var id = await scheduler.EnqueueAsync<DeclaredJob>(options => options.WithRetries(1), AbortToken);

        // then
        var stored = await _TimeJobAsync(provider, id);
        stored.Retries.Should().Be(1);
        stored.RetryIntervals.Should().BeNull();
        var definition = provider.GetRequiredService<JobFunctionRegistry>().GetFailurePolicy(_Declared);
        definition.ImmediateRetries.Should().Be(2);
        definition.DelayedRetries.Should().Be(3);
        definition.ShouldFail(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public async Task should_store_no_retries_when_no_policy_is_set_anywhere()
    {
        // given
        await using var provider = _Provider();
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var id = await scheduler.EnqueueAsync<UndeclaredJob>(AbortToken);

        // then
        var stored = await _TimeJobAsync(provider, id);
        stored.Retries.Should().Be(0);
        stored.RetryIntervals.Should().BeNull();
        provider.GetRequiredService<JobFunctionRegistry>().GetFailurePolicy(_Undeclared).TotalAttempts.Should().Be(1);
    }

    [Fact]
    public async Task should_apply_the_host_default_only_to_a_job_that_declares_no_policy()
    {
        // given
        await using var provider = _Provider(options => options.DefaultFailurePolicy<OneDelayedRetryPolicy>());
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var undeclared = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<UndeclaredJob>(AbortToken));
        var declared = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<DeclaredJob>(AbortToken));

        // then
        undeclared.Retries.Should().Be(1);
        undeclared.RetryIntervals.Should().Equal(10);
        declared.Retries.Should().Be(5);
    }

    [Fact]
    public async Task should_apply_an_inline_host_default()
    {
        // given
        await using var provider = _Provider(options => options.DefaultFailurePolicy(policy => policy.Immediate(2)));
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var stored = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<UndeclaredJob>(AbortToken));

        // then
        stored.Retries.Should().Be(2);
        stored.RetryIntervals.Should().Equal(0, 0);
    }

    [Fact]
    public async Task should_let_tuning_replace_a_declared_policy()
    {
        // given
        await using var provider = _Provider(options =>
            options
                .Tune(_Declared, job => job.FailurePolicy<OneDelayedRetryPolicy>())
                .Tune(_Undeclared, job => job.FailurePolicy(policy => policy.Immediate(1)))
        );
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var declared = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<DeclaredJob>(AbortToken));
        var undeclared = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<UndeclaredJob>(AbortToken));

        // then
        declared.Retries.Should().Be(1);
        declared.RetryIntervals.Should().Equal(10);
        undeclared.Retries.Should().Be(1);
        undeclared.RetryIntervals.Should().Equal(0);
        var registry = provider.GetRequiredService<JobFunctionRegistry>();
        registry.GetFailurePolicy(_Declared).ShouldFail(new TimeoutException()).Should().BeFalse();
    }

    [Fact]
    public async Task should_let_configuration_override_the_numbers_and_keep_the_fail_rules()
    {
        // given
        await using var provider = _Provider(
            configuration: _Configuration(
                ($"Headless:Jobs:Jobs:{_Declared}:FailurePolicy:ImmediateRetries", "0"),
                ($"Headless:Jobs:Jobs:{_Declared}:FailurePolicy:DelayedRetries", "2"),
                ($"Headless:Jobs:Jobs:{_Declared}:FailurePolicy:DelayedInitialDelay", "00:00:05"),
                ($"Headless:Jobs:Jobs:{_Declared}:FailurePolicy:DelayedMaxDelay", "00:00:06")
            )
        );
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var stored = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<DeclaredJob>(AbortToken));

        // then
        stored.Retries.Should().Be(2);
        stored.RetryIntervals.Should().Equal(5, 6);
        var definition = provider.GetRequiredService<JobFunctionRegistry>().GetFailurePolicy(_Declared);
        definition.ShouldFail(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public async Task should_apply_configuration_after_tuning()
    {
        // given
        await using var provider = _Provider(
            options => options.Tune(_Declared, job => job.FailurePolicy<OneDelayedRetryPolicy>()),
            _Configuration(($"Headless:Jobs:Jobs:{_Declared}:FailurePolicy:ImmediateRetries", "1"))
        );
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var stored = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<DeclaredJob>(AbortToken));

        // then
        stored.Retries.Should().Be(2);
        stored.RetryIntervals.Should().Equal(0, 10);
    }

    [Fact]
    public async Task should_round_a_sub_second_delay_up_to_whole_seconds()
    {
        // given
        await using var provider = _Provider(options =>
            options.Tune(
                _Undeclared,
                job =>
                    job.FailurePolicy(policy =>
                        policy.Delayed(2, TimeSpan.FromMilliseconds(1200), TimeSpan.FromSeconds(10))
                    )
            )
        );
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var stored = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<UndeclaredJob>(AbortToken));

        // then: 1.2 seconds rounds up to 2, and its doubling, 2.4 seconds, rounds up to 3.
        stored.Retries.Should().Be(2);
        stored.RetryIntervals.Should().Equal(2, 3);
    }

    [Theory]
    [InlineData("defaults")]
    [InlineData("request")]
    [InlineData("tune")]
    public void should_fail_startup_when_a_startup_policy_sets_retries(string source)
    {
        // given
        var services = new ServiceCollection();

        // when
        var register = () =>
            services.AddHeadlessJobs(options =>
            {
                options.DisableBackgroundServices();
                _ = source switch
                {
                    "defaults" => options.ConfigureDefaults(job => job.WithRetries(3)),
                    "request" => options.ConfigureJob<PolicyArgs>(job => job.WithRetryIntervals(2, 5)),
                    _ => options.Tune(_Declared, job => job.Options(policy => policy.WithRetries(2))),
                };
            });

        // then
        register.Should().Throw<ArgumentException>().WithMessage("*FailurePolicy*");
    }

    [Fact]
    public async Task should_keep_the_node_death_setting_of_a_startup_policy()
    {
        // given
        await using var provider = _Provider(options =>
            options.ConfigureDefaults(job => job.WithNodeDeathPolicy(NodeDeathPolicy.Skip))
        );
        var scheduler = provider.GetRequiredService<IJobScheduler>();

        // when
        var stored = await _TimeJobAsync(provider, await scheduler.EnqueueAsync<DeclaredJob>(AbortToken));

        // then
        stored.OnNodeDeath.Should().Be(NodeDeathPolicy.Skip);
        stored.Retries.Should().Be(5);
    }

    [Theory]
    [InlineData("FailurePolicy:Jitter", "0.5", "Headless:Jobs:Jobs:policy.declared:FailurePolicy:Jitter")]
    [InlineData(
        "FailurePolicy:ImmediateRetries",
        "many",
        "Headless:Jobs:Jobs:policy.declared:FailurePolicy:ImmediateRetries"
    )]
    [InlineData(
        "FailurePolicy:DelayedInitialDelay",
        "soon",
        "Headless:Jobs:Jobs:policy.declared:FailurePolicy:DelayedInitialDelay"
    )]
    [InlineData("FailurePolicy:ImmediateRetries", "-1", "Headless:Jobs:Jobs:policy.declared:FailurePolicy")]
    [InlineData("FailurePolicy:DelayedMaxDelay", "00:00:01", "Headless:Jobs:Jobs:policy.declared:FailurePolicy")]
    public async Task should_fail_startup_naming_the_path_of_an_invalid_failure_policy_setting(
        string key,
        string value,
        string path
    )
    {
        // given
        await using var provider = _Provider(
            configuration: _Configuration(($"Headless:Jobs:Jobs:{_Declared}:{key}", value))
        );

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve.Should().Throw<InvalidOperationException>().WithMessage($"*'{path}'*");
    }

    [Fact]
    public async Task should_fail_startup_when_a_declared_policy_factory_returns_null()
    {
        // given
        await using var provider = _Provider(module: ModuleKind.NullFactory);

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve.Should().Throw<InvalidOperationException>().WithMessage($"*'{_Declared}'*");
    }

    [Fact]
    public async Task should_seed_a_cron_definition_with_the_flattened_policy_only_when_it_is_created()
    {
        // given
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> store;
        await using (var first = _Provider(module: ModuleKind.Nightly))
        {
            store = first.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

            // when
            await _StartAsync(first);
        }

        var seeded = (await store.GetCronJobsAsync(predicate: null, AbortToken)).Single(x =>
            string.Equals(x.Function, _Nightly, StringComparison.Ordinal)
        );

        await using (var second = _Provider(module: ModuleKind.NightlyChanged, store: store))
        {
            await _StartAsync(second);
        }

        // then
        seeded.Retries.Should().Be(5);
        seeded.RetryIntervals.Should().Equal(0, 0, 30, 60, 120);
        var reseeded = (await store.GetCronJobsAsync(predicate: null, AbortToken)).Single(x =>
            string.Equals(x.Function, _Nightly, StringComparison.Ordinal)
        );
        reseeded.Retries.Should().Be(5);
        reseeded.RetryIntervals.Should().Equal(0, 0, 30, 60, 120);
    }

    private static async Task _StartAsync(IServiceProvider provider)
    {
        var initializer = provider.GetServices<IHostedService>().OfType<JobsInitializationHostedService>().Single();
        await initializer.StartAsync(AbortToken);
    }

    private static async Task<TimeJobEntity> _TimeJobAsync(IServiceProvider provider, Guid id)
    {
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var stored = await store.GetTimeJobByIdAsync(id, AbortToken);
        stored.Should().NotBeNull();
        return stored!;
    }

    private static IConfiguration _Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

    private static ServiceProvider _Provider(
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? configure = null,
        IConfiguration? configuration = null,
        ModuleKind module = ModuleKind.Default,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? store = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (configuration is not null)
        {
            services.AddSingleton(configuration);
        }

        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            _ = module switch
            {
                ModuleKind.NullFactory => options.AddModule<NullFactoryJobsModule>(),
                ModuleKind.Nightly => options.AddModule<NightlyJobsModule>(),
                ModuleKind.NightlyChanged => options.AddModule<NightlyChangedJobsModule>(),
                _ => options.AddModule<PolicyJobsModule>(),
            };
            configure?.Invoke(options);
        });

        if (store is not null)
        {
            services.AddSingleton(store);
        }

        return services.BuildServiceProvider();
    }

    public enum ModuleKind
    {
        Default = 0,
        NullFactory = 1,
        Nightly = 2,
        NightlyChanged = 3,
    }

    private static void _Add(
        JobsCatalogBuilder catalog,
        string identity,
        Type jobType,
        Func<FailurePolicy>? failurePolicy,
        string cron = ""
    )
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [identity] = new()
                {
                    CronExpression = cron,
                    Priority = JobPriority.Normal,
                    Delegate = static (_, _, _) => Task.CompletedTask,
                    MaxConcurrency = 0,
                    JobType = jobType,
                    FailurePolicy = failurePolicy,
                },
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [identity] = new(identity, null, cron, JobPriority.Normal, 0),
            }
        );
    }

    public sealed record PolicyArgs(string Value);

    /// <summary>2 immediate retries, then 3 delayed retries from 30 seconds capped at 15 minutes.</summary>
    public sealed class TieredPolicy : FailurePolicy
    {
        protected override void Configure(FailurePolicyBuilder policy) =>
            policy
                .Immediate(2)
                .Delayed(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15))
                .FailOn<TimeoutException>();
    }

    public sealed class OneDelayedRetryPolicy : FailurePolicy
    {
        protected override void Configure(FailurePolicyBuilder policy) =>
            policy.Delayed(1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public sealed class DeclaredJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class UndeclaredJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class NightlyJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class PolicyJobsModule : IJobsModule
    {
        private PolicyJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            _Add(catalog, _Declared, typeof(DeclaredJob), static () => new TieredPolicy());
            _Add(catalog, _Undeclared, typeof(UndeclaredJob), failurePolicy: null);
        }
    }

    public sealed class NullFactoryJobsModule : IJobsModule
    {
        private NullFactoryJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
            _Add(catalog, _Declared, typeof(DeclaredJob), static () => null!);
    }

    public sealed class NightlyJobsModule : IJobsModule
    {
        private NightlyJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
            _Add(catalog, _Nightly, typeof(NightlyJob), static () => new TieredPolicy(), cron: "0 0 3 * * *");
    }

    public sealed class NightlyChangedJobsModule : IJobsModule
    {
        private NightlyChangedJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
            _Add(catalog, _Nightly, typeof(NightlyJob), static () => new OneDelayedRetryPolicy(), cron: "0 0 3 * * *");
    }
}
