// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// An ordinary edit may only touch a job no node has started: an idle unclaimed row, or a queued row whose claim the
/// edit releases. A stale copy of a running or finished row must never overwrite what the executing node writes.
/// </summary>
public sealed class GuardedTimeJobUpdateProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private const string _NodeB = "node-b";
    private static readonly DateTimeOffset _Now = new(2026, 06, 17, 12, 00, 00, TimeSpan.Zero);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(JobStatus.Idle)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Succeeded)]
    [InlineData(JobStatus.DueDone)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Skipped)]
    public async Task should_refuse_and_change_nothing_when_the_row_is_owned_running_or_finished(JobStatus status)
    {
        var (provider, _) = _Create();
        var job = _TimeJob(status, _NodeA, _Now.UtcDateTime.Add(_Lease));
        job.RetryCount = 2;
        await provider.AddTimeJobsAsync([job], AbortToken);
        var before = _Snapshot((await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!);

        var staleCopy = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        staleCopy.Description = "edited";
        staleCopy.ExecutionTime = _Now.UtcDateTime.AddHours(1);
        staleCopy.Status = JobStatus.Idle;
        staleCopy.RetryCount = 0;

        var edit = async () => await provider.UpdateTimeJobsAsync([staleCopy], AbortToken);

        await edit.Should().ThrowAsync<InvalidOperationException>();
        _Snapshot((await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!).Should().Be(before);
    }

    [Fact]
    public async Task should_apply_the_edit_and_keep_stored_runtime_state_when_the_row_is_idle_and_unowned()
    {
        var (provider, time) = _Create();
        var job = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        job.RetryCount = 1;
        await provider.AddTimeJobsAsync([job], AbortToken);

        var copy = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        copy.Description = "edited";
        copy.Status = JobStatus.Succeeded;
        copy.RetryCount = 9;
        copy.OwnerId = _NodeB;
        copy.ExceptionMessage = "forged";
        time.Advance(TimeSpan.FromSeconds(3));

        (await provider.UpdateTimeJobsAsync([copy], AbortToken)).Should().Be(1);

        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Description.Should().Be("edited");
        stored.Status.Should().Be(JobStatus.Idle);
        stored.RetryCount.Should().Be(1);
        stored.OwnerId.Should().BeNull();
        stored.ExceptionMessage.Should().BeNull();
        stored.UpdatedAt.Should().Be(_Now.AddSeconds(3));
    }

    [Fact]
    public async Task should_release_a_queued_claim_so_the_former_owner_cannot_start_it()
    {
        var (provider, _) = _Create();
        var job = _TimeJob(JobStatus.Queued, _NodeA, _Now.UtcDateTime.Add(_Lease));
        await provider.AddTimeJobsAsync([job], AbortToken);
        var newTime = _Now.UtcDateTime.AddHours(2);

        var copy = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        copy.ExecutionTime = newTime;
        (await provider.UpdateTimeJobsAsync([copy], AbortToken)).Should().Be(1);

        var stored = (await provider.GetTimeJobByIdAsync(job.Id, AbortToken))!;
        stored.Status.Should().Be(JobStatus.Idle);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
        stored.ExecutionTime.Should().Be(newTime);

        var start = new JobExecutionState { FunctionName = string.Empty, ClaimOwner = _NodeA }.SetProperty(
            x => x.Status,
            JobStatus.InProgress
        );
        (await provider.UpdateTimeJobsWithUnifiedContextAsync([job.Id], start, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_roll_back_every_row_when_one_row_in_the_batch_is_refused()
    {
        var (provider, _) = _Create();
        var editable = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        var running = _TimeJob(JobStatus.InProgress, _NodeA, _Now.UtcDateTime.Add(_Lease));
        await provider.AddTimeJobsAsync([editable, running], AbortToken);

        var editableCopy = (await provider.GetTimeJobByIdAsync(editable.Id, AbortToken))!;
        editableCopy.Description = "edited";
        var runningCopy = (await provider.GetTimeJobByIdAsync(running.Id, AbortToken))!;
        runningCopy.Description = "edited";

        var edit = async () => await provider.UpdateTimeJobsAsync([editableCopy, runningCopy], AbortToken);

        await edit.Should().ThrowAsync<InvalidOperationException>();
        (await provider.GetTimeJobByIdAsync(editable.Id, AbortToken))!.Description.Should().BeNull();
        (await provider.GetTimeJobByIdAsync(running.Id, AbortToken))!.Description.Should().BeNull();
    }

    [Fact]
    public async Task should_insert_a_new_child_attached_to_an_idle_root()
    {
        var (provider, _) = _Create();
        var root = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);
        await provider.AddTimeJobsAsync([root], AbortToken);

        var copy = (await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!;
        var child = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            RunCondition = RunCondition.OnSuccess,
        };
        copy.Children.Add(child);

        (await provider.UpdateTimeJobsAsync([copy], AbortToken)).Should().Be(2);

        var storedChild = await provider.GetTimeJobByIdAsync(child.Id, AbortToken);
        storedChild.Should().NotBeNull();
        storedChild!.ParentId.Should().Be(root.Id);
    }

    [Fact]
    public async Task should_edit_a_preclaimed_chain_root_and_release_its_owned_descendants()
    {
        var (provider, _) = _Create();
        var (root, child, grandChild) = _Chain(rootOwner: _NodeA, descendantOwner: _NodeA);
        await provider.AddTimeJobsAsync([root], AbortToken);

        var copy = (await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!;
        copy.Description = "edited";
        copy.Children.Single().Description = "edited child";

        (await provider.UpdateTimeJobsAsync([copy], AbortToken)).Should().Be(3);

        foreach (var id in new[] { root.Id, child.Id, grandChild.Id })
        {
            var stored = (await provider.GetTimeJobByIdAsync(id, AbortToken))!;
            stored.Status.Should().Be(JobStatus.Idle);
            stored.OwnerId.Should().BeNull();
            stored.LockedUntil.Should().BeNull();
        }

        (await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!.Description.Should().Be("edited child");
    }

    [Fact]
    public async Task should_refuse_the_whole_edit_when_a_carried_descendant_belongs_to_another_owner()
    {
        var (provider, _) = _Create();
        var (root, child, _) = _Chain(rootOwner: _NodeA, descendantOwner: _NodeB);
        await provider.AddTimeJobsAsync([root], AbortToken);
        var rootBefore = _Snapshot((await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!);
        var childBefore = _Snapshot((await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!);

        var copy = (await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!;
        copy.Description = "edited";

        var edit = async () => await provider.UpdateTimeJobsAsync([copy], AbortToken);

        await edit.Should().ThrowAsync<InvalidOperationException>();
        _Snapshot((await provider.GetTimeJobByIdAsync(root.Id, AbortToken))!).Should().Be(rootBefore);
        _Snapshot((await provider.GetTimeJobByIdAsync(child.Id, AbortToken))!).Should().Be(childBefore);
    }

    [Fact]
    public async Task should_refuse_an_edit_of_a_row_that_does_not_exist()
    {
        var (provider, _) = _Create();
        var missing = _TimeJob(JobStatus.Idle, owner: null, lockedUntil: null);

        var edit = async () => await provider.UpdateTimeJobsAsync([missing], AbortToken);

        await edit.Should().ThrowAsync<InvalidOperationException>();
        (await provider.GetTimeJobByIdAsync(missing.Id, AbortToken)).Should().BeNull();
    }

    private static (FakeTimeJob Root, FakeTimeJob Child, FakeTimeJob GrandChild) _Chain(
        string rootOwner,
        string descendantOwner
    )
    {
        var lease = _Now.UtcDateTime.Add(_Lease);
        var grandChild = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Idle,
            OwnerId = descendantOwner,
            LockedUntil = lease,
            RunCondition = RunCondition.OnSuccess,
        };
        var child = new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Idle,
            OwnerId = descendantOwner,
            LockedUntil = lease,
            RunCondition = RunCondition.OnSuccess,
            Children = [grandChild],
        };
        var root = _TimeJob(JobStatus.Queued, rootOwner, lease);
        root.Children = [child];
        return (root, child, grandChild);
    }

    private static (
        JobStatus Status,
        string? OwnerId,
        DateTime? LockedUntil,
        int RetryCount,
        string? Description,
        DateTime? ExecutionTime,
        DateTimeOffset UpdatedAt
    ) _Snapshot(FakeTimeJob job) =>
        (job.Status, job.OwnerId, job.LockedUntil, job.RetryCount, job.Description, job.ExecutionTime, job.UpdatedAt);

    private static (JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> Provider, FakeTimeProvider Time) _Create()
    {
        var time = new FakeTimeProvider(_Now);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHeadlessGuidGenerator();
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = _Lease });
        var sp = services.BuildServiceProvider();
        return (new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp), time);
    }

    private static FakeTimeJob _TimeJob(JobStatus status, string? owner, DateTime? lockedUntil)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = status,
            OwnerId = owner,
            LockedUntil = lockedUntil,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = _Now.UtcDateTime.AddMinutes(10),
            UpdatedAt = _Now,
        };
    }
}
