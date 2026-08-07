// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.DashboardDtos;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

public sealed class JobsDashboardRepositoryBehaviorTests : TestBase
{
    private static readonly DateTimeOffset _Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_preserve_order_pagination_filters_and_combined_counts_when_querying_jobs()
    {
        using var fixture = new Fixture();
        var recentSucceeded = fixture.TimeJob(
            JobStatus.Succeeded,
            _Now.AddDays(-1),
            _Now.AddMinutes(-1),
            ownerId: "Node-A"
        );
        var recentFailed = fixture.TimeJob(JobStatus.Failed, _Now.AddDays(-2), _Now.AddMinutes(-2), ownerId: "node-a");
        var oldQueued = fixture.TimeJob(JobStatus.Queued, _Now.AddDays(-20), _Now.AddMinutes(-3));
        await fixture.Provider.AddTimeJobsAsync([oldQueued, recentFailed, recentSucceeded], AbortToken);

        var cronA = fixture.CronJob("cron-a", _Now.AddMinutes(-1));
        var cronB = fixture.CronJob("cron-b", _Now.AddMinutes(-2));
        await fixture.Provider.InsertCronJobsAsync([cronB, cronA], AbortToken);

        var recentDueDone = fixture.Occurrence(
            cronA,
            JobStatus.DueDone,
            _Now.AddDays(-1),
            _Now.AddMinutes(-1),
            ownerId: "NODE-A"
        );
        var recentCronFailed = fixture.Occurrence(cronA, JobStatus.Failed, _Now.AddDays(-3), _Now.AddMinutes(-2));
        var oldCronSucceeded = fixture.Occurrence(cronB, JobStatus.Succeeded, _Now.AddDays(-20), _Now.AddMinutes(-3));
        await fixture.Provider.InsertCronJobOccurrencesAsync(
            [oldCronSucceeded, recentCronFailed, recentDueDone],
            AbortToken
        );

        (await fixture.Repository.GetTimeJobsAsync(AbortToken))
            .Select(x => x.Id)
            .Should()
            .Equal(recentSucceeded.Id, recentFailed.Id, oldQueued.Id);
        var timePage = await fixture.Repository.GetTimeJobsPaginatedAsync(2, 1, AbortToken);
        timePage.Items.Should().ContainSingle().Which.Id.Should().Be(recentFailed.Id);
        timePage.TotalCount.Should().Be(3);
        timePage.PageNumber.Should().Be(2);
        timePage.PageSize.Should().Be(1);

        (await fixture.Repository.GetCronJobsAsync(AbortToken)).Select(x => x.Id).Should().Equal(cronA.Id, cronB.Id);
        var cronPage = await fixture.Repository.GetCronJobsPaginatedAsync(1, 1, AbortToken);
        cronPage.Items.Should().ContainSingle().Which.Id.Should().Be(cronA.Id);
        cronPage.TotalCount.Should().Be(2);

        (await fixture.Repository.GetCronJobsOccurrencesAsync(cronA.Id, AbortToken))
            .Select(x => x.Id)
            .Should()
            .Equal(recentDueDone.Id, recentCronFailed.Id);
        var occurrencePage = await fixture.Repository.GetCronJobsOccurrencesPaginatedAsync(cronA.Id, 2, 1, AbortToken);
        occurrencePage.Items.Should().ContainSingle().Which.Id.Should().Be(recentCronFailed.Id);
        occurrencePage.TotalCount.Should().Be(2);

        var allStatuses = Enum.GetValues<JobStatus>();
        var timeStatuses = await fixture.Repository.GetTimeJobFullDataAsync(AbortToken);
        timeStatuses.Select(result => result.Status).Should().BeEquivalentTo(allStatuses);
        timeStatuses.Should().Contain((JobStatus.Succeeded, 1));
        timeStatuses.Should().Contain((JobStatus.Failed, 1));
        timeStatuses.Should().Contain((JobStatus.Queued, 1));
        timeStatuses.Should().Contain((JobStatus.Cancelled, 0));

        var cronStatuses = await fixture.Repository.GetCronJobFullDataAsync(AbortToken);
        cronStatuses.Select(result => result.Status).Should().BeEquivalentTo(allStatuses);
        cronStatuses.Should().Contain((JobStatus.DueDone, 1));
        cronStatuses.Should().Contain((JobStatus.Failed, 1));
        cronStatuses.Should().Contain((JobStatus.Succeeded, 1));

        (await fixture.Repository.GetLastWeekJobStatusesAsync(AbortToken)).Should().Equal((0, 2), (1, 2), (2, 4));
        (await fixture.Repository.GetOverallJobStatusesAsync(AbortToken))
            .ToDictionary(x => x.Item1, x => x.Item2)
            .Should()
            .BeEquivalentTo(
                new Dictionary<JobStatus, int>
                {
                    [JobStatus.Succeeded] = 2,
                    [JobStatus.Failed] = 2,
                    [JobStatus.Queued] = 1,
                    [JobStatus.DueDone] = 1,
                }
            );

        (await fixture.Repository.GetMachineJobsAsync(AbortToken))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(("Node-A", 3));
    }

    [Fact]
    public async Task should_filter_and_zero_fill_graph_data_when_range_and_cron_id_are_supplied()
    {
        using var fixture = new Fixture();
        var cronA = fixture.CronJob("cron-a", _Now);
        var cronB = fixture.CronJob("cron-b", _Now.AddMinutes(-1));
        await fixture.Provider.InsertCronJobsAsync([cronA, cronB], AbortToken);

        await fixture.Provider.AddTimeJobsAsync(
            [
                fixture.TimeJob(JobStatus.Succeeded, _Now.AddDays(-1), _Now),
                fixture.TimeJob(JobStatus.Failed, _Now, _Now.AddMinutes(-1)),
                fixture.TimeJob(JobStatus.Succeeded, _Now.AddDays(-2), _Now.AddMinutes(-2)),
            ],
            AbortToken
        );

        await fixture.Provider.InsertCronJobOccurrencesAsync(
            [
                fixture.Occurrence(cronA, JobStatus.Succeeded, _Now.AddDays(-1), _Now),
                fixture.Occurrence(cronA, JobStatus.Failed, _Now.AddDays(1), _Now.AddMinutes(-1)),
                fixture.Occurrence(cronB, JobStatus.Failed, _Now, _Now.AddMinutes(-2)),
                fixture.Occurrence(cronA, JobStatus.Succeeded, _Now.AddDays(-2), _Now.AddMinutes(-3)),
            ],
            AbortToken
        );

        var timeGraph = await fixture.Repository.GetTimeJobsGraphSpecificDataAsync(-1, 1, AbortToken);
        timeGraph.Select(x => x.Date).Should().Equal(_Now.AddDays(-1).Date, _Now.Date, _Now.AddDays(1).Date);
        timeGraph[0].Results.Should().Contain(new JobStatusCount(JobStatus.Succeeded, 1));
        timeGraph[1].Results.Should().Contain(new JobStatusCount(JobStatus.Failed, 1));
        timeGraph[2].Results.Should().OnlyContain(x => x.Count == 0);

        var cronGraph = await fixture.Repository.GetCronJobsGraphSpecificDataAsync(-1, 1, AbortToken);
        cronGraph[0].Results.Should().Contain(new JobStatusCount(JobStatus.Succeeded, 1));
        cronGraph[1].Results.Should().Contain(new JobStatusCount(JobStatus.Failed, 1));
        cronGraph[2].Results.Should().Contain(new JobStatusCount(JobStatus.Failed, 1));

        var cronById = await fixture.Repository.GetCronJobsGraphSpecificDataByIdAsync(cronA.Id, -1, 1, AbortToken);
        cronById[1].Results.Should().OnlyContain(x => x.Count == 0);
        cronById[2].Results.Should().Contain(new JobStatusCount(JobStatus.Failed, 1));

        var allStatuses = Enum.GetValues<JobStatus>();
        foreach (var day in timeGraph.Concat(cronGraph).Concat(cronById))
        {
            day.Results.Select(result => result.Status).Should().BeEquivalentTo(allStatuses);
        }

        (await fixture.Repository.GetTimeJobsGraphSpecificDataAsync(2, -2, AbortToken)).Should().BeEmpty();
        (await fixture.Repository.GetCronJobsGraphSpecificDataAsync(2, -2, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_clamp_graph_range_when_day_offsets_exceed_the_supported_limit()
    {
        using var fixture = new Fixture();
        var cron = fixture.CronJob("cron", _Now);
        await fixture.Provider.InsertCronJobsAsync([cron], AbortToken);

        var timeGraph = await fixture.Repository.GetTimeJobsGraphSpecificDataAsync(
            int.MinValue,
            int.MaxValue,
            AbortToken
        );
        var cronGraph = await fixture.Repository.GetCronJobsGraphSpecificDataAsync(
            int.MinValue,
            int.MaxValue,
            AbortToken
        );
        var cronById = await fixture.Repository.GetCronJobsGraphSpecificDataByIdAsync(
            cron.Id,
            int.MinValue,
            int.MaxValue,
            AbortToken
        );

        foreach (var graph in new[] { timeGraph, cronGraph, cronById })
        {
            graph.Should().HaveCount(733);
            graph[0].Date.Should().Be(_Now.AddDays(-366).Date);
            graph[^1].Date.Should().Be(_Now.AddDays(366).Date);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly FakeTimeProvider _timeProvider = new(_Now);

        public Fixture()
        {
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(_timeProvider);
            services.AddHeadlessGuidGenerator();
            services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a" });
            _services = services.BuildServiceProvider();

            Provider = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(_services);
            Repository = new JobsDashboardRepository<TimeJobEntity, CronJobEntity>(
                new JobsExecutionContext(),
                Provider,
                Substitute.For<IJobsHostScheduler>(),
                Substitute.For<IJobsNotificationHubSender>(),
                new DashboardOptionsBuilder(),
                Substitute.For<IJobsDispatcher>(),
                JobFunctionRegistryBuilder.Build([], [], []),
                _timeProvider,
                _services.GetRequiredService<IGuidGenerator>(),
                _services,
                JobsRequestSerializationOptions.Default
            );
        }

        public JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> Provider { get; }

        public JobsDashboardRepository<TimeJobEntity, CronJobEntity> Repository { get; }

        public TimeJobEntity TimeJob(
            JobStatus status,
            DateTimeOffset executionTime,
            DateTimeOffset createdAt,
            string? ownerId = null
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                Function = "time-job",
                Status = status,
                ExecutionTime = executionTime.UtcDateTime,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                OwnerId = ownerId,
                LockedUntil = ownerId is null ? null : _Now.AddMinutes(5).UtcDateTime,
            };

        public CronJobEntity CronJob(string function, DateTimeOffset createdAt) =>
            new()
            {
                Id = Guid.NewGuid(),
                Function = function,
                Expression = "0 * * * *",
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            };

        public CronJobOccurrenceEntity<CronJobEntity> Occurrence(
            CronJobEntity cronJob,
            JobStatus status,
            DateTimeOffset executionTime,
            DateTimeOffset createdAt,
            string? ownerId = null
        ) =>
            new()
            {
                Id = Guid.NewGuid(),
                CronJobId = cronJob.Id,
                CronJob = cronJob,
                Status = status,
                ExecutionTime = executionTime.UtcDateTime,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                OwnerId = ownerId,
                LockedUntil = ownerId is null ? null : _Now.AddMinutes(5).UtcDateTime,
            };

        public void Dispose()
        {
            _services.Dispose();
        }
    }
}
