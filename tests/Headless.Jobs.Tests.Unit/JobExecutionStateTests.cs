using Headless.Jobs;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Tests;

public sealed class JobExecutionStateTests
{
    [Fact]
    public void SetProperty_Tracks_Updated_Properties()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };

        context
            .SetProperty(c => c.Status, JobStatus.InProgress)
            .SetProperty(c => c.ElapsedTime, 123L)
            .SetProperty(c => c.ReleaseLock, true);

        var updated = context.PropertiesToUpdate;

        updated
            .Should()
            .Contain(
                new[]
                {
                    nameof(JobExecutionState.Status),
                    nameof(JobExecutionState.ElapsedTime),
                    nameof(JobExecutionState.ReleaseLock),
                }
            );

        updated.Should().HaveCount(3);
        context.Status.Should().Be(JobStatus.InProgress);
        context.ElapsedTime.Should().Be(123L);
        context.ReleaseLock.Should().BeTrue();
    }

    [Fact]
    public void ResetUpdateProps_Clears_Tracked_Properties()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };

        context.SetProperty(c => c.Status, JobStatus.Succeeded).SetProperty(c => c.ElapsedTime, 500L);

        context.PropertiesToUpdate.Should().NotBeEmpty();

        context.ResetUpdateProps();

        context.PropertiesToUpdate.Should().BeEmpty();
    }

    [Fact]
    public void ResetUpdateProps_Does_Not_Reset_Property_Values()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };

        context.SetProperty(c => c.Status, JobStatus.Succeeded).SetProperty(c => c.ElapsedTime, 250L);

        context.ResetUpdateProps();

        context.Status.Should().Be(JobStatus.Succeeded);
        context.ElapsedTime.Should().Be(250L);
        context.PropertiesToUpdate.Should().BeEmpty();
    }

    [Fact]
    public void PropertiesToUpdate_Defaults_To_Empty_Set_And_Tracks_Updates()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };

        context.PropertiesToUpdate.Should().BeEmpty();

        context.SetProperty(c => c.Status, JobStatus.InProgress);

        var updated = context.PropertiesToUpdate;
        updated.Should().NotBeNull();
        updated.Should().Contain(nameof(JobExecutionState.Status));
    }

    [Fact]
    public void SetProperty_Allows_Multiple_Updates_To_Same_Property()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };

        context.SetProperty(c => c.Status, JobStatus.InProgress).SetProperty(c => c.Status, JobStatus.Failed);

        context.Status.Should().Be(JobStatus.Failed);

        var updated = context.PropertiesToUpdate;
        updated.Should().Contain(nameof(JobExecutionState.Status));
        updated.Should().ContainSingle();
    }

    [Fact]
    public void SetProperty_Throws_For_NonProperty_Expression()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };

        Action act = () => context.SetProperty(c => c.ElapsedTime + 1, 10L);

        act.Should().Throw<ArgumentException>().WithMessage("*Expression must point to a property*");
    }

    [Fact]
    public void TimeJobChildren_Defaults_To_Empty_List_And_Can_Add()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };

        context.TimeJobChildren.Should().NotBeNull();
        context.TimeJobChildren.Should().BeEmpty();

        var child = new JobExecutionState
        {
            JobId = Guid.NewGuid(),
            ParentId = Guid.NewGuid(),
            FunctionName = "ChildFunction",
        };

        context.TimeJobChildren.Add(child);

        context.TimeJobChildren.Should().ContainSingle();
        context.TimeJobChildren.Single().FunctionName.Should().Be("ChildFunction");
    }

    [Fact]
    public void CachedDelegate_And_Priority_Can_Be_Assigned()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };
        JobFunctionDelegate handler = (_, _, _) => Task.CompletedTask;

        context.CachedDelegate = handler;
        context.CachedPriority = JobPriority.High;

        context.CachedDelegate.Should().BeSameAs(handler);
        context.CachedPriority.Should().Be(JobPriority.High);
    }

    [Fact]
    public void SetProperty_Supports_Array_And_String_Properties()
    {
        var context = new JobExecutionState() { FunctionName = "Test" };
        var intervals = new[] { 1, 5, 10 };
        const string exceptionDetails = "Something went wrong";

        context.SetProperty(c => c.RetryIntervals, intervals).SetProperty(c => c.ExceptionDetails, exceptionDetails);

        context.RetryIntervals.Should().BeSameAs(intervals);
        context.ExceptionDetails.Should().Be(exceptionDetails);

        var updated = context.PropertiesToUpdate;
        updated
            .Should()
            .Contain(new[] { nameof(JobExecutionState.RetryIntervals), nameof(JobExecutionState.ExceptionDetails) });
    }
}
