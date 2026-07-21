// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Managers;

/// <summary>
/// U3: building a queued time-job execution context must recurse the whole hydrated tree, not stop at the
/// grandchild level, carrying each descendant's own <c>RunCondition</c>/<c>RetryCount</c>/<c>ParentId</c> so a
/// deep chain is not silently truncated (or its retry budget reset) after restart.
/// </summary>
public sealed class InternalJobsManagerContextDepthTests : TestBase
{
    public sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    public sealed class FakeCronJob : CronJobEntity;

    private static InternalJobsManager<FakeTimeJob, FakeCronJob> _Manager(
        IJobPersistenceProvider<FakeTimeJob, FakeCronJob> provider
    )
    {
        return new InternalJobsManager<FakeTimeJob, FakeCronJob>(
            provider,
            new FakeTimeProvider(new DateTimeOffset(2026, 06, 17, 12, 00, 00, TimeSpan.Zero)),
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<FakeTimeJob, FakeCronJob>>.Instance,
            JobsRequestSerializationOptions.Default,
            Substitute.For<IGuidGenerator>()
        );
    }

    [Fact]
    public async Task run_timed_out_tickers_builds_the_execution_context_to_full_depth()
    {
        var provider = Substitute.For<IJobPersistenceProvider<FakeTimeJob, FakeCronJob>>();
        var manager = _Manager(provider);

        // A five-node linear chain: root -> c1 -> c2 -> c3 -> c4. Each descendant carries a distinct RunCondition
        // and RetryCount so a level that is dropped (or whose retry budget is reset) is caught.
        var c4 = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "c4",
            RunCondition = RunCondition.OnFailure,
            RetryCount = 4,
        };
        var c3 = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "c3",
            RunCondition = RunCondition.OnSuccess,
            RetryCount = 3,
            Children = [c4],
        };
        var c2 = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "c2",
            RunCondition = RunCondition.OnSuccess,
            RetryCount = 2,
            Children = [c3],
        };
        var c1 = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "c1",
            RunCondition = RunCondition.OnSuccess,
            RetryCount = 1,
            Children = [c2],
        };
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "root",
            ExecutionTime = DateTime.UtcNow,
            Children = [c1],
        };

        // Real hydration carries ParentId on every node; set it so the context assertion mirrors production.
        c1.ParentId = root.Id;
        c2.ParentId = c1.Id;
        c3.ParentId = c2.Id;
        c4.ParentId = c3.Id;

        provider.QueueTimedOutTimeJobsAsync(Arg.Any<CancellationToken>()).Returns(new[] { root }.ToAsyncEnumerable());
        provider
            .QueueTimedOutCronJobOccurrencesAsync(Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<CronJobOccurrenceEntity<FakeCronJob>>());

        var contexts = await manager.RunTimedOutTickers(AbortToken);

        var rootContext = contexts.Should().ContainSingle().Subject;
        var seeds = new[] { c1, c2, c3, c4 };
        var node = rootContext;
        foreach (var seed in seeds)
        {
            var childContext = node.TimeJobChildren.Should().ContainSingle().Subject;
            childContext.JobId.Should().Be(seed.Id);
            childContext.FunctionName.Should().Be(seed.Function);
            childContext.RunCondition.Should().Be(seed.RunCondition);
            childContext.RetryCount.Should().Be(seed.RetryCount);
            childContext.ParentId.Should().Be(node.JobId);
            node = childContext;
        }

        node.TimeJobChildren.Should().BeEmpty("the deepest node has no children");
    }
}
