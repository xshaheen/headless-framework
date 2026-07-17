using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;

#pragma warning disable REFL017 // Don't use name of wrong member
namespace Tests;

public sealed class JobsOptionsBuilderTests
{
    [Fact]
    public void scheduler_long_running_concurrency_defaults_to_four_or_max_concurrency()
    {
        var options = new SchedulerOptionsBuilder { MaxConcurrency = 2 };

        options.MaxLongRunningConcurrency.Should().Be(2);

        options.MaxConcurrency = 16;
        options.MaxLongRunningConcurrency.Should().Be(4);

        options.MaxLongRunningConcurrency = 7;
        options.MaxConcurrency = 1;
        options.MaxLongRunningConcurrency.Should().Be(7);
    }

    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private sealed class FakeExceptionHandler : IJobExceptionHandler
    {
        public Task HandleExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }

        public Task HandleCanceledExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        )
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void configure_retries_exposes_direct_polly_options()
    {
        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(
            new JobsExecutionContext(),
            new SchedulerOptionsBuilder()
        );
        var strategy = new RetryStrategyOptions
        {
            MaxRetryAttempts = 7,
            Delay = TimeSpan.FromSeconds(2),
            ShouldHandle = static _ => ValueTask.FromResult(true),
        };

        builder.ConfigureRetries(options => options.RetryStrategy = strategy);

        builder.RetryOptions.RetryStrategy.Should().BeSameAs(strategy);
    }

    [Fact]
    public void configure_request_json_options_initializes_and_invokes_config()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        builder.ConfigureRequestJsonOptions(options =>
        {
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        var jsonOptions =
            typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
                .GetProperty(
                    nameof(JobsOptionsBuilder<,>.RequestJsonSerializerOptions),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )!
                .GetValue(builder) as JsonSerializerOptions;

        jsonOptions.Should().NotBeNull();
        jsonOptions.PropertyNameCaseInsensitive.Should().BeTrue();
        jsonOptions.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.WhenWritingNull);
    }

    [Fact]
    public void use_g_zip_compression_sets_flag()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        builder.UseGZipCompression();

        var flag = typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
            .GetProperty(
                nameof(JobsOptionsBuilder<,>.RequestGZipCompressionEnabled),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);

        flag.Should().BeOfType<bool>().Which.Should().BeTrue();
    }

    [Fact]
    public void use_g_zip_compression_sets_expanded_size_limit()
    {
        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(
            new JobsExecutionContext(),
            new SchedulerOptionsBuilder()
        );

        builder.UseGZipCompression(1234);

        var limit = typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
            .GetProperty(
                nameof(JobsOptionsBuilder<,>.RequestGZipMaxDecompressedBytes),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);

        limit.Should().Be(1234);
        var act = () => builder.UseGZipCompression(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ignore_seed_defined_cron_jobs_disables_seeding_flag()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        builder.IgnoreSeedDefinedCronJobs();

        var flag = typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
            .GetProperty(
                nameof(JobsOptionsBuilder<,>.SeedDefinedCronJobs),
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);

        flag.Should().BeOfType<bool>().Which.Should().BeFalse();
    }

    [Fact]
    public void set_exception_handler_sets_handler_type()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        builder.SetExceptionHandler<FakeExceptionHandler>();

        var handlerType =
            typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
                .GetProperty(
                    nameof(JobsOptionsBuilder<,>.JobExceptionHandlerType),
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )!
                .GetValue(builder) as Type;

        handlerType.Should().Be<FakeExceptionHandler>();
    }

    [Fact]
    public void use_jobs_seeder_time_sets_time_seeder_action()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        builder.UseJobsSeeder(async (ITimeJobManager<FakeTimeJob> _) => await Task.CompletedTask);

        var seeder = typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
            .GetProperty(
                nameof(JobsOptionsBuilder<,>.TimeSeederAction),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);

        seeder.Should().NotBeNull();
    }

    [Fact]
    public void use_jobs_seeder_cron_sets_cron_seeder_action()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        builder.UseJobsSeeder(
            async (ICronJobManager<FakeCronJob> _) =>
            {
                await Task.CompletedTask;
            }
        );

        var seeder = typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
            .GetProperty(
                nameof(JobsOptionsBuilder<,>.CronSeederAction),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);

        seeder.Should().NotBeNull();
    }

    [Fact]
    public void configure_scheduler_invokes_delegate()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        builder.ConfigureScheduler(options =>
        {
            options.MaxConcurrency = 42;
            options.NodeId = "test-node";
        });

        schedulerOptions.MaxConcurrency.Should().Be(42);
        schedulerOptions.NodeId.Should().Be("test-node");
    }

    [Fact]
    public void default_node_id_is_machine_name()
    {
        // In-memory single-process identity only; the durable path stamps with Coordination's node@incarnation.
        var schedulerOptions = new SchedulerOptionsBuilder();

        schedulerOptions.NodeId.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void default_lease_duration_is_5_minutes()
    {
        var schedulerOptions = new SchedulerOptionsBuilder();

        schedulerOptions.LeaseDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void default_post_commit_drain_timeout_is_30_seconds()
    {
        var schedulerOptions = new SchedulerOptionsBuilder();

        schedulerOptions.PostCommitDrainTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(301)]
    public void add_headless_jobs_rejects_post_commit_drain_timeout_outside_valid_range(int timeoutSeconds)
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessJobs(options =>
                options.ConfigureScheduler(scheduler =>
                    scheduler.PostCommitDrainTimeout = TimeSpan.FromSeconds(timeoutSeconds)
                )
            );

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void add_headless_jobs_accepts_maximum_post_commit_drain_timeout()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessJobs(options =>
                options.ConfigureScheduler(scheduler => scheduler.PostCommitDrainTimeout = TimeSpan.FromMinutes(5))
            );

        act.Should().NotThrow();
    }

    [Fact]
    public void explicit_node_id_is_preserved_verbatim()
    {
        var schedulerOptions = new SchedulerOptionsBuilder { NodeId = "explicit-node" };

        schedulerOptions.NodeId.Should().Be("explicit-node");
    }

    [Fact]
    public void disable_background_services_sets_flag_to_false()
    {
        var executionContext = new JobsExecutionContext();
        var schedulerOptions = new SchedulerOptionsBuilder();

        var builder = new JobsOptionsBuilder<FakeTimeJob, FakeCronJob>(executionContext, schedulerOptions);

        // Default should be true
        var defaultFlag = typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
            .GetProperty(
                nameof(JobsOptionsBuilder<,>.RegisterBackgroundServices),
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);
        defaultFlag.Should().BeOfType<bool>().Which.Should().BeTrue();

        // After calling DisableBackgroundServices, should be false
        builder.DisableBackgroundServices();

        var flag = typeof(JobsOptionsBuilder<FakeTimeJob, FakeCronJob>)
            .GetProperty(
                nameof(JobsOptionsBuilder<,>.RegisterBackgroundServices),
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
            )!
            .GetValue(builder);

        flag.Should().BeOfType<bool>().Which.Should().BeFalse();
    }
}
