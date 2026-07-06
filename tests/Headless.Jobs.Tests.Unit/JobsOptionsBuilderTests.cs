using System.Reflection;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;

#pragma warning disable REFL017 // Don't use name of wrong member
namespace Tests;

public sealed class JobsOptionsBuilderTests
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private sealed class FakeExceptionHandler : IJobExceptionHandler
    {
        public Task HandleExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task HandleCanceledExceptionAsync(
            Exception exception,
            Guid jobId,
            JobType jobType,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    [Fact]
    public void ConfigureRequestJsonOptions_Initializes_And_Invokes_Config()
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
    public void UseGZipCompression_Sets_Flag()
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
    public void IgnoreSeedDefinedCronJobs_Disables_Seeding_Flag()
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
    public void SetExceptionHandler_Sets_Handler_Type()
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
    public void UseJobsSeeder_Time_Sets_TimeSeederAction()
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
    public void UseJobsSeeder_Cron_Sets_CronSeederAction()
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
    public void ConfigureScheduler_Invokes_Delegate()
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
    public void Default_NodeId_Is_MachineName()
    {
        // In-memory single-process identity only; the durable path stamps with Coordination's node@incarnation.
        var schedulerOptions = new SchedulerOptionsBuilder();

        schedulerOptions.NodeId.Should().Be(Environment.MachineName);
    }

    [Fact]
    public void Default_LeaseDuration_Is_5_Minutes()
    {
        var schedulerOptions = new SchedulerOptionsBuilder();

        schedulerOptions.LeaseDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Explicit_NodeId_Is_Preserved_Verbatim()
    {
        var schedulerOptions = new SchedulerOptionsBuilder { NodeId = "explicit-node" };

        schedulerOptions.NodeId.Should().Be("explicit-node");
    }

    [Fact]
    public void DisableBackgroundServices_Sets_Flag_To_False()
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
