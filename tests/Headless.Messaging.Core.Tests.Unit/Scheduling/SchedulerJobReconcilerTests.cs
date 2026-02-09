// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Scheduling;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Scheduling;

public sealed class SchedulerJobReconcilerTests : TestBase, IDisposable
{
    private readonly ScheduledJobDefinitionRegistry _registry = new();
    private readonly IScheduledJobStorage _storage = Substitute.For<IScheduledJobStorage>();
    private readonly CronScheduleCache _cronCache = new();

    public void Dispose() => _cronCache.Dispose();

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly ILogger<SchedulerJobReconciler> _logger;
    private readonly IConfiguration _configuration;

    public SchedulerJobReconcilerTests()
    {
        _logger = LoggerFactory.CreateLogger<SchedulerJobReconciler>();
        _configuration = new ConfigurationBuilder().Build();
    }

    [Fact]
    public async Task should_insert_new_jobs_not_in_database()
    {
        // given
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "new-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 0 * * *",
                TimeZone = "UTC",
            }
        );

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[]);

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, _configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Name == "new-job"
                    && j.CronExpression == "0 0 0 * * *"
                    && j.Type == ScheduledJobType.Recurring
                    && j.Status == ScheduledJobStatus.Pending
                    && j.IsEnabled
                    && j.NextRunTime != null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_upsert_job_with_updated_cron_expression()
    {
        // given
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "updated-job",
                ConsumerType = typeof(object),
                CronExpression = "0 */10 * * * *",
                TimeZone = "UTC",
            }
        );

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[]);

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, _configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then — upsert is called with the new cron expression
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j => j.Name == "updated-job" && j.CronExpression == "0 */10 * * * *"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_soft_disable_removed_jobs()
    {
        // given — a definition for a different job exists, DB has an orphaned enabled recurring job
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "active-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 0 * * *",
                TimeZone = "UTC",
            }
        );

        var existingJob = _CreateJob("orphan-job");
        existingJob.IsEnabled = true;
        existingJob.Type = ScheduledJobType.Recurring;

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[existingJob]);

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, _configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then
        await _storage
            .Received(1)
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Name == "orphan-job"
                    && j.Status == ScheduledJobStatus.Disabled
                    && !j.IsEnabled
                    && j.NextRunTime == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_disable_existing_matching_jobs()
    {
        // given
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "existing-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 0 * * *",
                TimeZone = "UTC",
            }
        );

        var existingJob = _CreateJob("existing-job");
        existingJob.IsEnabled = true;
        existingJob.Type = ScheduledJobType.Recurring;

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[existingJob]);

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, _configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then — upsert called for the definition, but UpdateJobAsync not called to disable
        await _storage
            .Received(1)
            .UpsertJobAsync(Arg.Is<ScheduledJob>(j => j.Name == "existing-job"), Arg.Any<CancellationToken>());
        await _storage
            .DidNotReceive()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j => j.Name == "existing-job" && j.Status == ScheduledJobStatus.Disabled),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_skip_reconciliation_when_no_definitions_registered()
    {
        // given — no definitions added to registry
        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, _configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then — no storage calls
        await _storage.DidNotReceive().UpsertJobAsync(Arg.Any<ScheduledJob>(), Arg.Any<CancellationToken>());
        await _storage.DidNotReceive().GetAllJobsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_preserve_retry_intervals_from_definition()
    {
        // given
        int[] retryIntervals = [1000, 5000, 30000];
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "retry-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 * * * *",
                TimeZone = "UTC",
                RetryIntervals = retryIntervals,
                SkipIfRunning = false,
            }
        );

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[]);

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, _configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j =>
                    j.Name == "retry-job"
                    && j.RetryIntervals != null
                    && j.RetryIntervals.Length == 3
                    && !j.SkipIfRunning
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_disable_one_time_jobs_without_definitions()
    {
        // given — a one-time job in DB, no definitions
        var oneTimeJob = _CreateJob("one-time");
        oneTimeJob.Type = ScheduledJobType.OneTime;
        oneTimeJob.IsEnabled = true;

        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "some-other-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 0 * * *",
            }
        );

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[oneTimeJob]);

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, _configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then — one-time jobs should not be disabled by reconciler
        await _storage
            .DidNotReceive()
            .UpdateJobAsync(
                Arg.Is<ScheduledJob>(j => j.Name == "one-time" && j.Status == ScheduledJobStatus.Disabled),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_config_cron_when_override_present()
    {
        // given
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "config-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 0 * * *",
                TimeZone = "UTC",
            }
        );

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[]);

        var configData = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "Messaging:Scheduling:Jobs:config-job:CronExpression", "0 */15 * * * *" },
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j => j.Name == "config-job" && j.CronExpression == "0 */15 * * * *"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_fallback_to_attribute_cron_when_config_is_invalid()
    {
        // given
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "fallback-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 0 * * *",
                TimeZone = "UTC",
            }
        );

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[]);

        var configData = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { "Messaging:Scheduling:Jobs:fallback-job:CronExpression", "invalid-cron" },
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j => j.Name == "fallback-job" && j.CronExpression == "0 0 0 * * *"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_use_attribute_cron_when_no_config_override()
    {
        // given
        _registry.Add(
            new ScheduledJobDefinition
            {
                Name = "attribute-job",
                ConsumerType = typeof(object),
                CronExpression = "0 0 12 * * *",
                TimeZone = "UTC",
            }
        );

        _storage.GetAllJobsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScheduledJob>)[]);

        var configuration = new ConfigurationBuilder().Build();
        var sut = new SchedulerJobReconciler(_registry, _storage, _cronCache, _timeProvider, configuration, _logger);

        // when
        await sut.StartAsync(AbortToken);

        // then
        await _storage
            .Received(1)
            .UpsertJobAsync(
                Arg.Is<ScheduledJob>(j => j.Name == "attribute-job" && j.CronExpression == "0 0 12 * * *"),
                Arg.Any<CancellationToken>()
            );
    }

    // -- helpers --

    private ScheduledJob _CreateJob(string name)
    {
        var now = _timeProvider.GetUtcNow();
        return new ScheduledJob
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = ScheduledJobType.Recurring,
            CronExpression = "0 0 0 * * *",
            TimeZone = "UTC",
            Status = ScheduledJobStatus.Pending,
            NextRunTime = now.AddDays(1),
            RetryCount = 0,
            SkipIfRunning = true,
            IsEnabled = true,
            DateCreated = now,
            DateUpdated = now,
            MisfireStrategy = MisfireStrategy.FireImmediately,
        };
    }
}
