// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Authorization;
using Headless.Jobs.Endpoints;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Dashboard;

public sealed class JobsDashboardCancellationEndpointTests : TestBase
{
    [Theory]
    [InlineData(true, StatusCodes.Status200OK)]
    [InlineData(false, StatusCodes.Status400BadRequest)]
    public async Task cancellation_endpoint_forwards_the_request_and_maps_the_transition_result(
        bool accepted,
        int expectedStatus
    )
    {
        var scheduler = Substitute.For<IJobScheduler>();
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var jobId = Guid.NewGuid();
        scheduler.CancelAsync(jobId, AbortToken).Returns(accepted);
        persistence.GetTimeJobByIdAsync(jobId, AbortToken).Returns(new TimeJobEntity { Id = jobId });

        var result = await DashboardEndpoints.CancelJobAsync(
            jobId,
            new DefaultHttpContext(),
            scheduler,
            persistence,
            new JobsDashboardAuthorizer(new DashboardOptionsBuilder().WithNoAuth()),
            AbortToken
        );

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(expectedStatus);
        await scheduler.Received(1).CancelAsync(jobId, AbortToken);
    }
}
