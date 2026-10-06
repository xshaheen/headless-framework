// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Registration;

public sealed class JobsTuningTests : TestBase
{
    [Fact]
    public async Task should_change_concurrency_and_priority_in_the_frozen_registry()
    {
        // given
        var services = _Services();
        services.ConfigureJobs(jobs =>
            jobs.Tune(TestJobs.BillingCloseDay, job => job.Concurrency(2).Priority(JobPriority.High))
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var registry = provider.GetRequiredService<JobFunctionRegistry>();

        // then
        registry.Functions[TestJobs.BillingCloseDay].MaxConcurrency.Should().Be(2);
        registry.Functions[TestJobs.BillingCloseDay].Priority.Should().Be(JobPriority.High);
        registry.Descriptors[TestJobs.BillingCloseDay].MaxConcurrency.Should().Be(2);
        registry.Descriptors[TestJobs.BillingCloseDay].Priority.Should().Be(JobPriority.High);
        registry.DescriptorsByJobType[typeof(BillingCloseDay)].MaxConcurrency.Should().Be(2);
        registry.Functions[TestJobs.BillingSendInvoice].MaxConcurrency.Should().Be(0);
    }

    [Fact]
    public async Task should_apply_a_tune_recorded_before_the_module_that_declares_the_job()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureJobs(jobs => jobs.Tune(TestJobs.BillingCloseDay, job => job.Concurrency(3)));
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        services.ConfigureJobs(jobs => jobs.AddModule<BillingJobsModule>());
        await using var provider = services.BuildServiceProvider();

        // when
        var registry = provider.GetRequiredService<JobFunctionRegistry>();

        // then
        registry.Functions[TestJobs.BillingCloseDay].MaxConcurrency.Should().Be(3);
    }

    [Fact]
    public async Task should_let_a_later_tune_win_per_setting()
    {
        // given
        var services = _Services(options => options.Tune(TestJobs.BillingCloseDay, job => job.Concurrency(4)));
        services.ConfigureJobs(jobs =>
            jobs.Tune(TestJobs.BillingCloseDay, job => job.Priority(JobPriority.LongRunning))
                .Tune(TestJobs.BillingCloseDay, job => job.Concurrency(5))
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var registration = provider.GetRequiredService<JobFunctionRegistry>().Functions[TestJobs.BillingCloseDay];

        // then
        registration.MaxConcurrency.Should().Be(5);
        registration.Priority.Should().Be(JobPriority.LongRunning);
    }

    [Fact]
    public async Task should_fail_startup_naming_an_unknown_tuned_identity()
    {
        // given
        var services = _Services(options => options.Tune("billing.unknown", job => job.Concurrency(1)));
        await using var provider = services.BuildServiceProvider();

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve.Should().Throw<InvalidOperationException>().WithMessage("*'billing.unknown'*");
    }

    [Fact]
    public async Task should_resolve_tuned_node_death_options_for_the_job_only()
    {
        // given
        var services = _Services(options =>
            options
                .ConfigureDefaults(defaults => defaults.WithNodeDeathPolicy(NodeDeathPolicy.Skip))
                .Tune(
                    TestJobs.BillingCloseDay,
                    job => job.Options(policy => policy.WithNodeDeathPolicy(NodeDeathPolicy.MarkFailed))
                )
        );
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<JobFunctionRegistry>();

        // when
        var policies = provider.GetRequiredService<JobSchedulingPolicies>();

        // then
        policies
            .Resolve(registry.Descriptors[TestJobs.BillingCloseDay], call: null)
            .OnNodeDeath.Should()
            .Be(NodeDeathPolicy.MarkFailed);
        policies
            .Resolve(registry.Descriptors[TestJobs.BillingSendInvoice], call: null)
            .OnNodeDeath.Should()
            .Be(NodeDeathPolicy.Skip);
    }

    [Fact]
    public async Task should_reject_tuned_options_and_request_options_for_one_job()
    {
        // given
        var services = _Services(options =>
            options
                .ConfigureJob<InvoiceArgs>(policy => policy.WithNodeDeathPolicy(NodeDeathPolicy.Skip))
                .Tune(
                    TestJobs.BillingSendInvoice,
                    job => job.Options(new JobOptions { OnNodeDeath = NodeDeathPolicy.MarkFailed })
                )
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var resolve = () => provider.GetRequiredService<JobSchedulingPolicies>();

        // then
        resolve.Should().Throw<InvalidOperationException>().WithMessage($"*'{TestJobs.BillingSendInvoice}'*");
    }

    [Fact]
    public async Task should_run_tuned_execute_middleware_only_for_the_tuned_job()
    {
        // given
        var services = _Services(options =>
            options.Tune(TestJobs.BillingCloseDay, job => job.UseExecuteMiddleware<CountingExecuteMiddleware>())
        );
        services.AddSingleton<CountingExecuteMiddleware>();
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<JobFunctionRegistry>();
        var middleware = provider.GetRequiredService<CountingExecuteMiddleware>();

        // when
        await _ExecuteAsync(registry, provider, TestJobs.BillingCloseDay);
        await _ExecuteAsync(registry, provider, TestJobs.BillingSendInvoice);

        // then
        middleware.Functions.Should().Equal(TestJobs.BillingCloseDay);
    }

    [Fact]
    public async Task should_run_the_same_tuned_middleware_once_when_it_is_tuned_twice()
    {
        // given
        var services = _Services(options =>
            options
                .Tune(TestJobs.BillingCloseDay, job => job.UseExecuteMiddleware<CountingExecuteMiddleware>())
                .Tune(TestJobs.BillingCloseDay, job => job.UseExecuteMiddleware<CountingExecuteMiddleware>())
        );
        services.AddSingleton<CountingExecuteMiddleware>();
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<JobFunctionRegistry>();

        // when
        await _ExecuteAsync(registry, provider, TestJobs.BillingCloseDay);

        // then
        provider.GetRequiredService<CountingExecuteMiddleware>().Functions.Should().ContainSingle();
    }

    [Fact]
    public async Task should_bind_concurrency_and_priority_from_configuration_after_code_tuning()
    {
        // given
        var services = _Services(options => options.Tune(TestJobs.BillingCloseDay, job => job.Concurrency(2)));
        services.AddSingleton<IConfiguration>(
            _Configuration(
                ("Headless:Jobs:Jobs:billing.close-day:Concurrency", "3"),
                ("Headless:Jobs:Jobs:billing.close-day:Priority", "LongRunning")
            )
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var registration = provider.GetRequiredService<JobFunctionRegistry>().Functions[TestJobs.BillingCloseDay];

        // then
        registration.MaxConcurrency.Should().Be(3);
        registration.Priority.Should().Be(JobPriority.LongRunning);
    }

    [Fact]
    public async Task should_set_the_cluster_limit_from_tuning_and_let_configuration_override_it()
    {
        // given
        var services = _Services(options =>
            options.Tune(TestJobs.BillingCloseDay, job => job.ClusterConcurrency(2).Concurrency(1))
        );
        services.AddSingleton<IConfiguration>(
            _Configuration(("Headless:Jobs:Jobs:billing.close-day:ClusterConcurrency", "4"))
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var registry = provider.GetRequiredService<JobFunctionRegistry>();
        var limits = provider.GetRequiredService<JobsClusterConcurrency>();

        // then
        registry.Functions[TestJobs.BillingCloseDay].ClusterMaxConcurrency.Should().Be(4);
        registry.Functions[TestJobs.BillingCloseDay].MaxConcurrency.Should().Be(1);
        limits.LimitedFunctions.Should().Equal(TestJobs.BillingCloseDay);
        limits.LimitOf(TestJobs.BillingCloseDay).Should().Be(4);
        limits.IsLimited(TestJobs.BillingSendInvoice).Should().BeFalse();
    }

    [Fact]
    public async Task should_remove_the_cluster_limit_when_tuned_to_zero()
    {
        // given
        var services = _Services(options =>
            options
                .Tune(TestJobs.BillingCloseDay, job => job.ClusterConcurrency(3))
                .Tune(TestJobs.BillingCloseDay, job => job.ClusterConcurrency(0))
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var limits = provider.GetRequiredService<JobsClusterConcurrency>();

        // then
        limits.HasLimits.Should().BeFalse();
    }

    [Theory]
    [InlineData("Headless:Jobs:Jobs:billing.close-day:ClusterConcurrency", "-1", "ClusterConcurrency")]
    [InlineData("Headless:Jobs:Jobs:billing.unknown:Concurrency", "3", "billing.unknown")]
    [InlineData("Headless:Jobs:Jobs:billing.close-day:Concurrency", "-1", "Concurrency")]
    [InlineData("Headless:Jobs:Jobs:billing.close-day:Concurrency", "many", "Concurrency")]
    [InlineData("Headless:Jobs:Jobs:billing.close-day:Priority", "Urgent", "Priority")]
    [InlineData("Headless:Jobs:Jobs:billing.close-day:Priority", "2", "Priority")]
    [InlineData("Headless:Jobs:Jobs:billing.close-day:Retries", "3", "Retries")]
    public async Task should_fail_startup_on_invalid_job_configuration(string key, string value, string named)
    {
        // given
        var services = _Services();
        services.AddSingleton<IConfiguration>(_Configuration((key, value)));
        await using var provider = services.BuildServiceProvider();

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve.Should().Throw<InvalidOperationException>().WithMessage($"*{named}*");
    }

    [Fact]
    public void should_reject_an_invalid_tuning_value_when_it_is_authored()
    {
        var services = new ServiceCollection();

        var negative = () =>
            services.ConfigureJobs(jobs => jobs.Tune(TestJobs.BillingCloseDay, job => job.Concurrency(-1)));
        var negativeCluster = () =>
            services.ConfigureJobs(jobs => jobs.Tune(TestJobs.BillingCloseDay, job => job.ClusterConcurrency(-1)));
        var blank = () => services.ConfigureJobs(jobs => jobs.Tune(" ", job => job.Concurrency(1)));

        negative.Should().Throw<ArgumentOutOfRangeException>();
        negativeCluster.Should().Throw<ArgumentOutOfRangeException>();
        blank.Should().Throw<ArgumentException>();
    }

    private static ServiceCollection _Services(
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? configure = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.AddModule<BillingJobsModule>();
            configure?.Invoke(options);
        });
        return services;
    }

    private static IConfiguration _Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();

    private static async Task _ExecuteAsync(JobFunctionRegistry registry, IServiceProvider provider, string function)
    {
        await using var scope = provider.CreateAsyncScope();
        await registry.Middleware.DispatchExecuteAsync(
            new JobExecuteContext(
                registry.Descriptors[function],
                new JobExecutionState { FunctionName = function },
                new JobContext { FunctionName = function },
                attempt: 0,
                scope.ServiceProvider
            ),
            _ => Task.CompletedTask,
            AbortToken
        );
    }

    private sealed class CountingExecuteMiddleware : IJobExecuteMiddleware
    {
        public List<string> Functions { get; } = [];

        public Task InvokeAsync(
            JobExecuteContext context,
            JobExecuteNext next,
            CancellationToken cancellationToken = default
        )
        {
            Functions.Add(context.Descriptor.FunctionName);
            return next(cancellationToken);
        }
    }
}
