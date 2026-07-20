// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Endpoints;
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
        var jobId = Guid.NewGuid();
        scheduler.CancelAsync(jobId, AbortToken).Returns(accepted);

        var result = await DashboardEndpoints.CancelJobAsync(jobId, scheduler, AbortToken);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(expectedStatus);
        await scheduler.Received(1).CancelAsync(jobId, AbortToken);
    }
}
