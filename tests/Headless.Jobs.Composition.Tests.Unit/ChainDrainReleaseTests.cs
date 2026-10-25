// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Managers;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

/// <summary>
/// A chain root that a graceful stop hands back never ran, so the children that run while their parent runs must not
/// run either: the next owner re-runs the whole tree. Runs the real executor, manager, and in-memory store, and reads
/// the stored rows back.
/// </summary>
public sealed class ChainDrainReleaseTests : TestBase
{
    private const string _RootFunction = "chain.drain.root";
    private const string _ChildFunction = "chain.drain.child";

    // Only a failure guard: every scenario below ends on its own once the behavior holds.
    private static readonly TimeSpan _RunBound = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_not_run_an_in_progress_child_when_the_drain_releases_its_root(bool drainBeforeStart)
    {
        // given: a claimed root with one prior charged attempt and a child that runs while its parent runs. Either the
        // drain is already on when the root is picked up, or it begins after the root's first drain check while the
        // root confirms its owner
        await using var services = _Services();
        var store = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var manager = _Manager(services, store);
        await using var scheduler = new JobsTaskScheduler(maxConcurrency: 1, timeProvider: TimeProvider.System);
        var root = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _RootFunction,
            ExecutionTime = DateTime.UtcNow,
            Retries = 3,
            RetryCount = 1,
            RetryIntervals = [0],
        };
        var child = new TimeJobEntity
        {
            Id = Guid.NewGuid(),
            Function = _ChildFunction,
            ParentId = root.Id,
            RunCondition = RunCondition.InProgress,
            Retries = 0,
            RetryIntervals = [0],
        };
        (await store.AddTimeJobsAsync([root, child], AbortToken)).Should().Be(2);
        var claimed = (await store.AcquireImmediateTimeJobsAsync([root.Id], AbortToken)).Single();
        var identity = new StampOwnerIdentity(claimed.OwnerId!, drainBeforeStart ? null : scheduler.BeginDrain);
        var rootRan = false;
        var childRan = false;
        var context = _State(claimed, RunCondition.InProgress);
        context.TimeJobChildren.Add(_State(child, RunCondition.InProgress, claimed.OwnerId));
        var registry = _Registry(() => rootRan = true, () => childRan = true);
        JobsExecutionContext.CacheFunctionReferences(context, registry);
        JobsExecutionContext.CacheFunctionReferences(context.TimeJobChildren[0], registry);
        var handler = new JobsExecutionTaskHandler(
            services,
            TimeProvider.System,
            Substitute.For<IJobsInstrumentation>(),
            manager,
            registry,
            new JobsExecutionCancellationRegistry(),
            new SchedulerOptionsBuilder(),
            NullLogger<JobsExecutionTaskHandler>.Instance,
            new JobsFunctionTagBound(registry),
            taskScheduler: scheduler,
            ownerIdentity: identity
        );

        if (drainBeforeStart)
        {
            scheduler.BeginDrain();
        }

        // when
        await handler
            .ExecuteTaskAsync(context, isDue: false, cancellationToken: AbortToken)
            .WaitAsync(_RunBound, AbortToken);

        // then
        rootRan.Should().BeFalse();
        childRan.Should().BeFalse("a child must not run while its root is handed back unstarted");
        var storedRoot = await store.GetTimeJobByIdAsync(root.Id, AbortToken);
        storedRoot!.Status.Should().Be(JobStatus.Idle);
        storedRoot.RetryCount.Should().Be(1, "a graceful stop is not an attempt the job used up");
        storedRoot.OwnerId.Should().BeNull();
        var storedChild = await store.GetTimeJobByIdAsync(child.Id, AbortToken);
        storedChild!.Status.Should().Be(JobStatus.Idle);
        storedChild.ExecutedAt.Should().BeNull();
    }

    private static ServiceProvider _Services()
    {
        var registrations = new ServiceCollection();
        registrations.AddSingleton(TimeProvider.System);
        registrations.AddHeadlessGuidGenerator();
        return registrations.BuildServiceProvider();
    }

    private static InternalJobsManager<TimeJobEntity, CronJobEntity> _Manager(
        ServiceProvider services,
        JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity> store
    )
    {
        return new InternalJobsManager<TimeJobEntity, CronJobEntity>(
            store,
            TimeProvider.System,
            Substitute.For<IJobsNotificationHubSender>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            NullLogger<InternalJobsManager<TimeJobEntity, CronJobEntity>>.Instance,
            JobsRequestSerializationOptions.Default,
            services.GetRequiredService<IGuidGenerator>(),
            services,
            new SchedulerOptionsBuilder()
        );
    }

    private static JobFunctionRegistry _Registry(Action onRoot, Action onChild)
    {
        return JobFunctionRegistryBuilder.Build(
            [_Registration(_RootFunction, onRoot), _Registration(_ChildFunction, onChild)],
            [],
            [_Descriptor(_RootFunction), _Descriptor(_ChildFunction)]
        );
    }

    private static KeyValuePair<string, JobFunctionRegistration> _Registration(string function, Action onRun)
    {
        return new(
            function,
            new()
            {
                CronExpression = "",
                Priority = JobPriority.Normal,
                MaxConcurrency = 0,
                Delegate = (_, _, _) =>
                {
                    onRun();
                    return Task.CompletedTask;
                },
            }
        );
    }

    private static KeyValuePair<string, JobFunctionDescriptor> _Descriptor(string function)
    {
        return new(function, new(function, null, "", JobPriority.Normal, 0));
    }

    private static JobExecutionState _State(TimeJobEntity row, RunCondition runCondition, string? claimOwner = null)
    {
        return new JobExecutionState
        {
            JobId = row.Id,
            ParentId = row.ParentId,
            FunctionName = row.Function,
            ContractVersion = row.ContractVersion,
            Type = JobType.TimeJob,
            ExecutionTime = row.ExecutionTime ?? DateTime.UtcNow,
            Status = row.Status,
            RunCondition = runCondition,
            RetryCount = row.RetryCount,
            Retries = row.Retries,
            RetryIntervals = row.RetryIntervals,
            ClaimOwner = claimOwner ?? row.OwnerId,
        };
    }

    // A live identity that can run an action the first time a run confirms its owner, which is how a test begins the
    // drain after a run's first drain check and before its function starts.
    private sealed class StampOwnerIdentity(string owner, Action? onFirstStamp) : IJobsOwnerIdentity
    {
        private int _stamped;

        public string DisplayOwner => owner;

        public CancellationToken MembershipLostToken => CancellationToken.None;

        public bool TryGetStampOwner([NotNullWhen(true)] out string? stampOwner)
        {
            if (Interlocked.Exchange(ref _stamped, 1) == 0)
            {
                onFirstStamp?.Invoke();
            }

            stampOwner = owner;
            return true;
        }
    }
}
