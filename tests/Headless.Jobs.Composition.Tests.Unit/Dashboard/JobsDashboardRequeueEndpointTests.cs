// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Endpoints;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Tests.Dashboard;

public sealed class JobsDashboardRequeueEndpointTests : TestBase
{
    [Fact]
    public async Task time_job_requeue_returns_ok_when_the_row_moved()
    {
        var scheduler = Substitute.For<IJobScheduler>();
        var jobId = Guid.NewGuid();
        scheduler.RequeueAsync(jobId, AbortToken).Returns(JobRequeueOutcome.Requeued);

        var result = await DashboardEndpoints.RequeueJobAsync(jobId, scheduler, AbortToken);

        result.Should().BeOfType<Ok>();
        await scheduler.Received(1).RequeueAsync(jobId, AbortToken);
    }

    [Theory]
    [InlineData(JobRequeueOutcome.NotFound)]
    [InlineData(JobRequeueOutcome.NotFailed)]
    [InlineData(JobRequeueOutcome.ChainMember)]
    [InlineData(JobRequeueOutcome.SupersededGeneration)]
    [InlineData(JobRequeueOutcome.Conflict)]
    public async Task time_job_requeue_refusal_returns_bad_request_naming_the_reason(JobRequeueOutcome refusal)
    {
        var scheduler = Substitute.For<IJobScheduler>();
        var jobId = Guid.NewGuid();
        scheduler.RequeueAsync(jobId, AbortToken).Returns(refusal);

        var result = await DashboardEndpoints.RequeueJobAsync(jobId, scheduler, AbortToken);

        var badRequest = result.Should().BeOfType<BadRequest<string>>().Subject;
        badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        badRequest.Value.Should().Be(refusal.ToString());
    }

    [Fact]
    public async Task occurrence_requeue_returns_ok_when_the_row_moved()
    {
        var scheduler = Substitute.For<IJobScheduler>();
        var occurrenceId = Guid.NewGuid();
        scheduler.RequeueOccurrenceAsync(occurrenceId, AbortToken).Returns(JobRequeueOutcome.Requeued);

        var result = await DashboardEndpoints.RequeueCronJobOccurrenceAsync(occurrenceId, scheduler, AbortToken);

        result.Should().BeOfType<Ok>();
        await scheduler.Received(1).RequeueOccurrenceAsync(occurrenceId, AbortToken);
        await scheduler.DidNotReceive().RequeueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(JobRequeueOutcome.NotFailed)]
    [InlineData(JobRequeueOutcome.Overlap)]
    [InlineData(JobRequeueOutcome.Conflict)]
    public async Task occurrence_requeue_refusal_returns_bad_request_naming_the_reason(JobRequeueOutcome refusal)
    {
        var scheduler = Substitute.For<IJobScheduler>();
        var occurrenceId = Guid.NewGuid();
        scheduler.RequeueOccurrenceAsync(occurrenceId, AbortToken).Returns(refusal);

        var result = await DashboardEndpoints.RequeueCronJobOccurrenceAsync(occurrenceId, scheduler, AbortToken);

        result.Should().BeOfType<BadRequest<string>>().Which.Value.Should().Be(refusal.ToString());
    }
}
