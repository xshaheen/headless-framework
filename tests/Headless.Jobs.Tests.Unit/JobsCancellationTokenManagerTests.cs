// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Models;

namespace Tests;

public sealed class JobsCancellationTokenManagerTests
{
    [Fact]
    public void should_cancel_registered_job_and_remove_parent_state_when_requested_by_id()
    {
        // given
        var parentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        using var source = new CancellationTokenSource();
        var token = source.Token;
        var callbackInvoked = false;
        using var registration = token.Register(() => callbackInvoked = true);

        JobsCancellationTokenManager.AddTickerCancellationToken(source, _Job(jobId, parentId), isDue: false);

        try
        {
            // when
            var result = JobsCancellationTokenManager.RequestTickerCancellationById(jobId);

            // then
            result.Should().BeTrue();
            token.IsCancellationRequested.Should().BeTrue();
            callbackInvoked.Should().BeTrue();
            JobsCancellationTokenManager.IsParentRunning(parentId).Should().BeFalse();
            JobsCancellationTokenManager.RequestTickerCancellationById(jobId).Should().BeFalse();
        }
        finally
        {
            JobsCancellationTokenManager.RemoveTickerCancellationToken(jobId);
        }
    }

    [Fact]
    public void should_report_other_running_siblings_when_excluding_current_job()
    {
        // given
        var parentId = Guid.NewGuid();
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();
        using var firstSource = new CancellationTokenSource();
        using var secondSource = new CancellationTokenSource();

        JobsCancellationTokenManager.AddTickerCancellationToken(firstSource, _Job(firstJobId, parentId), isDue: false);
        JobsCancellationTokenManager.AddTickerCancellationToken(
            secondSource,
            _Job(secondJobId, parentId),
            isDue: false
        );

        try
        {
            // when / then
            JobsCancellationTokenManager.IsParentRunningExcludingSelf(parentId, firstJobId).Should().BeTrue();

            JobsCancellationTokenManager.RemoveTickerCancellationToken(secondJobId).Should().BeTrue();

            JobsCancellationTokenManager.IsParentRunningExcludingSelf(parentId, firstJobId).Should().BeFalse();
            JobsCancellationTokenManager.IsParentRunning(parentId).Should().BeTrue();
        }
        finally
        {
            JobsCancellationTokenManager.RemoveTickerCancellationToken(firstJobId);
            JobsCancellationTokenManager.RemoveTickerCancellationToken(secondJobId);
        }

        JobsCancellationTokenManager.IsParentRunning(parentId).Should().BeFalse();
    }

    [Fact]
    public async Task should_retain_all_siblings_when_jobs_register_concurrently()
    {
        const int registrationCount = 32;

        for (var survivorIndex = 0; survivorIndex < registrationCount; survivorIndex++)
        {
            // given
            var parentId = Guid.NewGuid();
            var registrations = Enumerable
                .Range(0, registrationCount)
                .Select(static _ => (JobId: Guid.NewGuid(), Source: new CancellationTokenSource()))
                .ToArray();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var registrationTasks = registrations
                    .Select(async registration =>
                    {
                        await start.Task;
                        JobsCancellationTokenManager.AddTickerCancellationToken(
                            registration.Source,
                            _Job(registration.JobId, parentId),
                            isDue: false
                        );
                    })
                    .ToArray();

                // when
                start.SetResult();
                await Task.WhenAll(registrationTasks);

                foreach (var (jobId, _) in registrations.Where((_, index) => index != survivorIndex))
                {
                    JobsCancellationTokenManager.RemoveTickerCancellationToken(jobId).Should().BeTrue();
                }

                // then
                JobsCancellationTokenManager.IsParentRunning(parentId).Should().BeTrue();
                JobsCancellationTokenManager
                    .RemoveTickerCancellationToken(registrations[survivorIndex].JobId)
                    .Should()
                    .BeTrue();
                JobsCancellationTokenManager.IsParentRunning(parentId).Should().BeFalse();
            }
            finally
            {
                foreach (var (jobId, source) in registrations)
                {
                    JobsCancellationTokenManager.RemoveTickerCancellationToken(jobId);
                    source.Dispose();
                }
            }
        }
    }

    [Fact]
    public void should_return_false_when_cancellation_is_requested_for_unknown_job()
    {
        JobsCancellationTokenManager.RequestTickerCancellationById(Guid.NewGuid()).Should().BeFalse();
    }

    private static JobExecutionState _Job(Guid jobId, Guid parentId)
    {
        return new JobExecutionState
        {
            JobId = jobId,
            ParentId = parentId,
            FunctionName = "test-function",
        };
    }
}
