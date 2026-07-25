// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// U4/R3: the in-memory provider persists <see cref="Headless.Jobs.Entities.BaseEntity.BaseJobEntity.TenantId"/> and
/// re-materializes it through every storage projection — the clone/hierarchy path (<c>_CloneTicker</c> via
/// <see cref="JobsInMemoryPersistenceProvider{TTimeJob,TCronJob}.GetTimeJobByIdAsync"/>) and the pickup projection
/// (<c>_ForQueueTimeJobs</c>) at all three chain levels, including the timed-out re-queue path (<see
/// cref="JobsInMemoryPersistenceProvider{TTimeJob,TCronJob}.QueueTimedOutTimeJobsAsync"/>). This is the
/// RetryCount-class regression guard: a projection that drops a per-node field silently restores system scope after
/// pickup.
/// </summary>
public sealed class TenantIdRoundTripProviderTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _NodeA = "node-a";
    private const string _RootTenant = "tenant-root";
    private const string _ChildTenant = "tenant-child";
    private const string _GrandChildTenant = "tenant-gchild";

    private static readonly DateTime _Now = new(2026, 06, 17, 12, 00, 00, DateTimeKind.Utc);
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(_Now, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = _NodeA, LeaseDuration = _Lease });
        var sp = services.BuildServiceProvider();
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(sp);
    }

    // A root that the timed-out fallback sweep will claim: Idle + unowned, ExecutionTime well past the 1s window.
    private static FakeTimeJob _RootTimeJob(DateTime executionTime)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Idle,
            OwnerId = null,
            LockedUntil = null,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = executionTime,
            TenantId = _RootTenant,
        };
    }

    // A chain descendant: null ExecutionTime so it is materialized as part of the parent's hierarchy projection.
    private static FakeTimeJob _DescendantTimeJob(string tenantId)
    {
        return new FakeTimeJob
        {
            Id = Guid.NewGuid(),
            Function = "fn",
            Status = JobStatus.Idle,
            OwnerId = null,
            LockedUntil = null,
            OnNodeDeath = NodeDeathPolicy.Retry,
            ExecutionTime = null,
            TenantId = tenantId,
        };
    }

    [Fact]
    public async Task get_time_job_by_id_round_trips_tenant_id_through_the_clone_hierarchy()
    {
        var provider = _Create();

        var grandChild = _DescendantTimeJob(_GrandChildTenant);
        var child = _DescendantTimeJob(_ChildTenant);
        child.Children.Add(grandChild);
        var root = _RootTimeJob(_Now.AddMinutes(-10));
        root.Children.Add(child);

        await provider.AddTimeJobsAsync([root], AbortToken);

        var fetched = await provider.GetTimeJobByIdAsync(root.Id, AbortToken);

        fetched.Should().NotBeNull();
        fetched.TenantId.Should().Be(_RootTenant);
        var fetchedChild = fetched.Children.Should().ContainSingle().Subject;
        fetchedChild.TenantId.Should().Be(_ChildTenant);
        fetchedChild.Children.Should().ContainSingle().Which.TenantId.Should().Be(_GrandChildTenant);
    }

    [Fact]
    public async Task queue_timed_out_time_jobs_round_trips_tenant_id_at_every_chain_level()
    {
        var provider = _Create();

        var grandChild = _DescendantTimeJob(_GrandChildTenant);
        var child = _DescendantTimeJob(_ChildTenant);
        child.Children.Add(grandChild);
        var root = _RootTimeJob(_Now.AddMinutes(-10));
        root.Children.Add(child);

        await provider.AddTimeJobsAsync([root], AbortToken);

        var swept = await provider.QueueTimedOutTimeJobsAsync(AbortToken).ToListAsync(AbortToken);

        var picked = swept.Should().ContainSingle().Subject;
        picked.TenantId.Should().Be(_RootTenant);
        var pickedChild = picked.Children.Should().ContainSingle().Subject;
        pickedChild.TenantId.Should().Be(_ChildTenant);
        pickedChild.Children.Should().ContainSingle().Which.TenantId.Should().Be(_GrandChildTenant);
    }

    [Fact]
    public async Task update_time_jobs_preserves_the_stored_tenant_when_the_payload_omits_it()
    {
        var provider = _Create();

        var root = _RootTimeJob(_Now.AddMinutes(-10));
        await provider.AddTimeJobsAsync([root], AbortToken);

        // Dashboard-style update payload: same row id, TenantId omitted (null).
        var update = _RootTimeJob(_Now.AddMinutes(5));
        update.Id = root.Id;
        update.TenantId = null;

        await provider.UpdateTimeJobsAsync([update], AbortToken);

        var fetched = await provider.GetTimeJobByIdAsync(root.Id, AbortToken);

        fetched.Should().NotBeNull();
        fetched.TenantId.Should().Be(_RootTenant);
    }
}
