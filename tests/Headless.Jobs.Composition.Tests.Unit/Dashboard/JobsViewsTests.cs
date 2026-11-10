// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;

namespace Tests.Dashboard;

public sealed class JobsViewsTests : TestBase
{
    [Fact]
    public void should_map_every_job_status_to_its_state_role()
    {
        // given: the role each status paints in, as the Runs view and the schedule strip draw it
        var expected = new Dictionary<JobStatus, string>
        {
            [JobStatus.Idle] = "sched",
            [JobStatus.Queued] = "enq",
            [JobStatus.InProgress] = "proc",
            [JobStatus.Succeeded] = "ok",
            [JobStatus.DueDone] = "ok",
            [JobStatus.Failed] = "fail",
            [JobStatus.Cancelled] = "off",
            [JobStatus.Skipped] = "off",
        };

        // when
        var roles = Enum.GetValues<JobStatus>().ToDictionary(status => status, JobsViews.StatusTone);

        // then: a new status fails here until it is given a role
        roles.Should().Equal(expected);
    }
}
