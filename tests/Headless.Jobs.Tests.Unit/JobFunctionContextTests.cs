using Headless.Jobs.Base;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class JobFunctionContextTests : Headless.Testing.Tests.TestBase
{
    [Fact]
    public void GenericContext_Preserves_ScheduledFor_From_Base_Context()
    {
        // given
        var scheduledFor = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var baseContext = new JobFunctionContext
        {
            Id = Guid.NewGuid(),
            Type = JobType.TimeJob,
            RetryCount = 1,
            IsDue = true,
            ScheduledFor = scheduledFor,
            FunctionName = "TestFunction",
            CronOccurrenceOperations = new CronOccurrenceOperations { SkipIfAlreadyRunningAction = () => { } },
        };

        var request = new TestRequest { Value = 42 };

        // when — no object initializer: the [SetsRequiredMembers] copy constructor must clone every base member.
        var genericContext = new JobFunctionContext<TestRequest>(baseContext, request);

        // then
        genericContext.Id.Should().Be(baseContext.Id);
        genericContext.Type.Should().Be(baseContext.Type);
        genericContext.RetryCount.Should().Be(baseContext.RetryCount);
        genericContext.IsDue.Should().Be(baseContext.IsDue);
        genericContext.ScheduledFor.Should().Be(baseContext.ScheduledFor);
        genericContext.FunctionName.Should().Be(baseContext.FunctionName);
        genericContext.CronOccurrenceOperations.Should().BeSameAs(baseContext.CronOccurrenceOperations);
        genericContext.Request.Should().Be(request);
    }

    [Fact]
    public async Task RequestCancellationAsync_routes_the_time_job_id_through_the_scoped_scheduler()
    {
        var scheduler = Substitute.For<IJobScheduler>();
        var id = Guid.NewGuid();
        scheduler.CancelAsync(id, AbortToken).Returns(true);
        var services = new ServiceCollection().AddSingleton(scheduler).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var context = new JobFunctionContext
        {
            Id = id,
            Type = JobType.TimeJob,
            FunctionName = "TestFunction",
            CronOccurrenceOperations = new CronOccurrenceOperations { SkipIfAlreadyRunningAction = () => { } },
        };
        context.SetServiceScope(scope);

        (await context.RequestCancellationAsync(AbortToken)).Should().BeTrue();

        await scheduler.Received(1).CancelAsync(id, AbortToken);
    }

    private sealed class TestRequest
    {
        public int Value { get; set; }
    }
}
