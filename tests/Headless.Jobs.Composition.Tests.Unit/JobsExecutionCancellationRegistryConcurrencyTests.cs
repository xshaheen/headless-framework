// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Models;
using Headless.Testing.Tests;

namespace Tests;

public sealed class JobsExecutionCancellationRegistryConcurrencyTests : TestBase
{
    [Fact]
    public void should_cancel_registered_job_and_remove_parent_state_when_signalled_by_handle()
    {
        var registry = new JobsExecutionCancellationRegistry();
        var parentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        using var source = new CancellationTokenSource();
        var callbackInvoked = false;
        using var callback = source.Token.Register(() => callbackInvoked = true);
        var registration = registry.Register(source, _Context(jobId, parentId));

        try
        {
            registry.TrySignalDurableCancellation(registration).Should().BeTrue();
            source.IsCancellationRequested.Should().BeTrue();
            callbackInvoked.Should().BeTrue();
            registry.IsParentRunning(parentId).Should().BeTrue();

            registry.TryRemove(registration).Should().BeTrue();
            registry.IsParentRunning(parentId).Should().BeFalse();
            registry.TrySignalDurableCancellation(registration).Should().BeFalse();
        }
        finally
        {
            registry.TryRemove(registration);
        }
    }

    [Fact]
    public void should_report_other_running_siblings_when_excluding_current_job()
    {
        var registry = new JobsExecutionCancellationRegistry();
        var parentId = Guid.NewGuid();
        using var firstSource = new CancellationTokenSource();
        using var secondSource = new CancellationTokenSource();
        var first = registry.Register(firstSource, _Context(Guid.NewGuid(), parentId));
        var second = registry.Register(secondSource, _Context(Guid.NewGuid(), parentId));

        try
        {
            registry.IsParentRunningExcludingSelf(parentId, first.JobId).Should().BeTrue();

            registry.TryRemove(second).Should().BeTrue();

            registry.IsParentRunningExcludingSelf(parentId, first.JobId).Should().BeFalse();
            registry.IsParentRunning(parentId).Should().BeTrue();
        }
        finally
        {
            registry.TryRemove(first);
            registry.TryRemove(second);
        }

        registry.IsParentRunning(parentId).Should().BeFalse();
    }

    [Fact]
    public async Task should_retain_all_siblings_when_jobs_register_concurrently()
    {
        const int registrationCount = 32;

        for (var survivorIndex = 0; survivorIndex < registrationCount; survivorIndex++)
        {
            var registry = new JobsExecutionCancellationRegistry();
            var parentId = Guid.NewGuid();
            var sources = Enumerable
                .Range(0, registrationCount)
                .Select(static _ => new CancellationTokenSource())
                .ToArray();
            JobsExecutionCancellationRegistration[] registrations = [];
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var registrationTasks = sources
                    .Select(async source =>
                    {
                        await start.Task.WaitAsync(AbortToken);
                        return registry.Register(source, _Context(Guid.NewGuid(), parentId));
                    })
                    .ToArray();

                start.SetResult();
                registrations = await Task.WhenAll(registrationTasks).WaitAsync(AbortToken);

                foreach (var registration in registrations.Where((_, index) => index != survivorIndex))
                {
                    registry.TryRemove(registration).Should().BeTrue();
                }

                registry.IsParentRunning(parentId).Should().BeTrue();
                registry.TryRemove(registrations[survivorIndex]).Should().BeTrue();
                registry.IsParentRunning(parentId).Should().BeFalse();
            }
            finally
            {
                foreach (var registration in registrations)
                {
                    registry.TryRemove(registration);
                }

                foreach (var source in sources)
                {
                    source.Dispose();
                }
            }
        }
    }

    [Fact]
    public void should_reject_an_unknown_registration_handle()
    {
        var registry = new JobsExecutionCancellationRegistry();
        using var source = new CancellationTokenSource();
        var unknown = new JobsExecutionCancellationRegistration(Guid.NewGuid(), Guid.Empty, source);

        registry.TrySignalDurableCancellation(unknown).Should().BeFalse();
        registry.TryRemove(unknown).Should().BeFalse();
        source.IsCancellationRequested.Should().BeFalse();
    }

    private static JobExecutionState _Context(Guid jobId, Guid parentId)
    {
        return new JobExecutionState
        {
            JobId = jobId,
            ParentId = parentId,
            FunctionName = "test-function",
        };
    }
}
