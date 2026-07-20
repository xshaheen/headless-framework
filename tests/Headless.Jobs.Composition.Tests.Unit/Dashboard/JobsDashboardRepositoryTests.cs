// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

public sealed class JobsDashboardRepositoryTests : TestBase
{
    private static readonly DateTime _Today = new(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

    public static TheoryData<int[], int, int> GraphDateCases =>
        new()
        {
            { [], -7, 8 },
            { Enumerable.Range(-10, 21).ToArray(), -11, 2 },
            { [-2, 2], -8, 7 },
            { [-1000, 1000], -1006, 1005 },
        };

    [Theory]
    [MemberData(nameof(GraphDateCases))]
    public async Task graph_preserves_the_history_derived_utc_date_sequence(
        int[] occurrenceDayOffsets,
        int expectedStartOffset,
        int expectedEndOffset
    )
    {
        var cronJobId = Guid.NewGuid();
        var dates = occurrenceDayOffsets.Select(offset => _Today.AddDays(offset)).ToArray();
        var range = CronOccurrenceGraphRangeSelector.Select(dates, _Today);
        var projectedCounts = dates
            .Where(date => date >= range.StartDate && date <= range.EndDate)
            .Select(date => new CronOccurrenceStatusCount
            {
                Date = date,
                Status = JobStatus.Succeeded,
                Count = 1,
            });
        var statusCounts = CronOccurrenceGraphRangeSelector.AddRangeBoundaries(projectedCounts, range);
        var (repository, persistence) = _CreateSut(statusCounts);

        var result = await repository.GetCronJobsOccurrencesGraphDataAsync(cronJobId, AbortToken);

        var expectedDates = Enumerable
            .Range(0, expectedEndOffset - expectedStartOffset + 1)
            .Select(offset => _Today.AddDays(expectedStartOffset + offset));
        result.Select(x => x.Date).Should().Equal(expectedDates);
        await persistence.Received(1).GetCronOccurrenceGraphStatusCountsAsync(cronJobId, _Today, AbortToken);
        await persistence
            .DidNotReceive()
            .GetAllCronJobOccurrencesAsync(
                Arg.Any<Expression<Func<CronJobOccurrenceEntity<CronJobEntity>, bool>>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task graph_preserves_projected_status_counts_and_zero_fills_missing_dates()
    {
        var cronJobId = Guid.NewGuid();
        CronOccurrenceStatusCount[] statusCounts =
        [
            new() { Date = _Today.AddDays(-1), IsRangeBoundary = true },
            new() { Date = _Today.AddDays(1), IsRangeBoundary = true },
            new()
            {
                Date = _Today,
                Status = JobStatus.Succeeded,
                Count = 3,
            },
            new()
            {
                Date = _Today,
                Status = JobStatus.Failed,
                Count = 2,
            },
        ];
        var (repository, _) = _CreateSut(statusCounts);

        var result = await repository.GetCronJobsOccurrencesGraphDataAsync(cronJobId, AbortToken);

        result.Select(x => x.Date).Should().Equal(_Today.AddDays(-1), _Today, _Today.AddDays(1));
#pragma warning disable RS0030 // JobGraphData intentionally retains its public Tuple-based compatibility contract.
        result[0].Results.Should().BeEmpty();
        result[1]
            .Results.Should()
            .BeEquivalentTo(
                new[] { Tuple.Create((int)JobStatus.Succeeded, 3), Tuple.Create((int)JobStatus.Failed, 2) }
            );
        result[2].Results.Should().BeEmpty();
#pragma warning restore RS0030
    }

    private static (
        JobsDashboardRepository<TimeJobEntity, CronJobEntity> Repository,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Persistence
    ) _CreateSut(CronOccurrenceStatusCount[] statusCounts)
    {
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        persistence
            .GetCronOccurrenceGraphStatusCountsAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(statusCounts));
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(_Today));
        var repository = new JobsDashboardRepository<TimeJobEntity, CronJobEntity>(
            new JobsExecutionContext(),
            persistence,
            Substitute.For<IJobsHostScheduler>(),
            Substitute.For<IJobsNotificationHubSender>(),
            new DashboardOptionsBuilder(),
            Substitute.For<IJobsDispatcher>(),
            JobFunctionRegistryBuilder.Build([], [], []),
            timeProvider,
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            Substitute.For<IServiceProvider>(),
            JobsRequestSerializationOptions.Default
        );

        return (repository, persistence);
    }
}
