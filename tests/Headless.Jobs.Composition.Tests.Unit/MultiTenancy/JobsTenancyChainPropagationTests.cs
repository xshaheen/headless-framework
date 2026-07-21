// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Exceptions;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.MultiTenancy;

[Collection<JobsHelperCollection>]
public sealed class JobsTenancyChainPropagationTests : TestBase, IDisposable
{
    private const string _Function = "chain-tenancy-fn";

    public JobsTenancyChainPropagationTests() => _RegisterFunction();

    public void Dispose() => JobFunctionProvider.ResetForTests();

    [Fact]
    public async Task two_level_chain_inherits_the_resolved_root_tenant()
    {
        var (manager, _) = _CreateManager(ambient: null);
        var grandChild = _Job();
        var child = _Job(children: [grandChild]);
        var root = _Job(tenantId: "t1", children: [child]);

        await manager.AddAsync(root, AbortToken);

        child.TenantId.Should().Be("t1");
        grandChild.TenantId.Should().Be("t1");
    }

    [Fact]
    public async Task a_pre_set_explicit_descendant_tenant_wins_over_root_inheritance()
    {
        var (manager, _) = _CreateManager(ambient: null);
        var grandChild = _Job();
        var child = _Job(tenantId: "t2", children: [grandChild]);
        var root = _Job(tenantId: "t1", children: [child]);

        await manager.AddAsync(root, AbortToken);

        child.TenantId.Should().Be("t2");
        // An unset grandchild inherits the ROOT's resolved tenant, not its parent's explicit value (KTD6).
        grandChild.TenantId.Should().Be("t1");
    }

    [Fact]
    public async Task a_descendant_system_job_under_an_ambient_tenant_is_rejected()
    {
        var (manager, _) = _CreateManager(ambient: "t1");
        var child = _Job(isSystemJob: true);
        var root = _Job(tenantId: "t1", children: [child]);

        var act = () => manager.AddAsync(root, AbortToken);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task a_descendant_system_job_without_an_ambient_tenant_stays_tenantless()
    {
        var (manager, _) = _CreateManager(ambient: null);
        var child = _Job(isSystemJob: true);
        var root = _Job(tenantId: "t1", children: [child]);

        await manager.AddAsync(root, AbortToken);

        child.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task each_batch_item_resolves_its_own_chain()
    {
        var (manager, _) = _CreateManager(ambient: null);
        var firstChild = _Job();
        var secondChild = _Job();
        var first = _Job(tenantId: "t1", children: [firstChild]);
        var second = _Job(tenantId: "t2", children: [secondChild]);

        await manager.AddBatchAsync([first, second], AbortToken);

        firstChild.TenantId.Should().Be("t1");
        secondChild.TenantId.Should().Be("t2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task a_blank_explicit_descendant_tenant_is_rejected(string blank)
    {
        var (manager, _) = _CreateManager(ambient: null);
        var child = _Job(tenantId: blank);
        var root = _Job(tenantId: "t1", children: [child]);

        var act = () => manager.AddAsync(root, AbortToken);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task an_over_length_explicit_descendant_tenant_is_rejected()
    {
        var (manager, _) = _CreateManager(ambient: null);
        var child = _Job(tenantId: new string('x', JobsTenancyOptions.TenantIdMaxLength + 1));
        var root = _Job(tenantId: "t1", children: [child]);

        var act = () => manager.AddAsync(root, AbortToken);

        await act.Should().ThrowAsync<JobValidatorException>();
    }

    [Fact]
    public async Task a_batch_aggregates_tenant_validation_and_function_errors_together()
    {
        // #278 finding #6: a batch that mixes an unknown-function failure with a mid-loop tenant-validation failure must
        // surface BOTH in the aggregated JobValidatorException, not just the one that threw first.
        var (manager, _) = _CreateManager(ambient: null);
        var unknownFunction = new TimeJobEntity
        {
            Function = "unknown-fn",
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };
        var blankChild = _Job(tenantId: "   ");
        var tenantFailure = _Job(tenantId: "t1", children: [blankChild]);

        var act = () => manager.AddBatchAsync([unknownFunction, tenantFailure], AbortToken);

        var exception = (await act.Should().ThrowAsync<JobValidatorException>()).Which;
        exception.Errors.Should().HaveCount(2);
        exception.Errors.Should().Contain(e => e.Contains("unknown-fn", StringComparison.Ordinal));
        exception.Errors.Should().Contain(e => e.Contains("blank", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task a_failed_batch_restores_the_captured_tenant_on_the_caller_entities()
    {
        // #278 finding #8: a rejected all-or-nothing batch writes nothing, so any tenant the schedule pipeline inherited
        // onto a caller entity mid-pass must be rolled back — otherwise a retry under a different ambient treats the
        // stale value as an explicit one.
        var (manager, _) = _CreateManager(ambient: null);
        var inheritedChild = _Job(); // unset tenant — inherits the root's "t1" during resolution
        var validRoot = _Job(tenantId: "t1", children: [inheritedChild]);
        var unknownFunction = new TimeJobEntity
        {
            Function = "unknown-fn",
            ExecutionTime = DateTime.UtcNow.AddHours(1),
        };

        var act = () => manager.AddBatchAsync([validRoot, unknownFunction], AbortToken);

        await act.Should().ThrowAsync<JobValidatorException>();
        inheritedChild.TenantId.Should().BeNull();
        validRoot.TenantId.Should().Be("t1");
    }

    [Fact]
    public async Task update_with_a_new_unset_descendant_inherits_the_stored_root_tenant()
    {
        // #278 finding #5: a descendant appended through UpdateAsync bypasses the Add path's resolution, so it must
        // inherit the STORED root tenant (immutable after schedule) even when the update payload omits the root tenant.
        var (manager, persistence) = _CreateManager(ambient: null);
        var storedRoot = _Job(tenantId: "t-stored");
        storedRoot.Id = Guid.NewGuid();
        persistence.GetTimeJobByIdAsync(storedRoot.Id, Arg.Any<CancellationToken>()).Returns(storedRoot);
        persistence.UpdateTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);

        var newChild = _Job(); // appended child, unset tenant
        var update = _Job(children: [newChild]); // payload omits the root tenant
        update.Id = storedRoot.Id;

        var result = await manager.UpdateAsync(update, AbortToken);

        result.IsSucceeded.Should().BeTrue();
        newChild.TenantId.Should().Be("t-stored");
    }

    [Fact]
    public async Task update_with_an_invalid_explicit_descendant_is_rejected_before_persistence()
    {
        // #278 finding #5: an explicit but invalid (blank) descendant tenant on an update is rejected exactly like the
        // Add path, before the row is written.
        var (manager, persistence) = _CreateManager(ambient: null);
        var storedRoot = _Job(tenantId: "t-stored");
        storedRoot.Id = Guid.NewGuid();
        persistence.GetTimeJobByIdAsync(storedRoot.Id, Arg.Any<CancellationToken>()).Returns(storedRoot);

        var blankChild = _Job(tenantId: "   ");
        var update = _Job(children: [blankChild]);
        update.Id = storedRoot.Id;

        var result = await manager.UpdateAsync(update, AbortToken);

        result.IsSucceeded.Should().BeFalse();
        result.Exception.Should().BeOfType<JobValidatorException>();
        await persistence.DidNotReceive().UpdateTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task update_without_children_does_no_stored_root_read()
    {
        // #278 finding #5: the childless update hot path must not pay for the stored-root read the chain resolution
        // needs.
        var (manager, persistence) = _CreateManager(ambient: null);
        persistence.UpdateTimeJobsAsync(Arg.Any<TimeJobEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        var update = _Job(tenantId: "t1");
        update.Id = Guid.NewGuid();

        await manager.UpdateAsync(update, AbortToken);

        await persistence.DidNotReceive().GetTimeJobByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static TimeJobEntity _Job(
        string? tenantId = null,
        bool isSystemJob = false,
        ICollection<TimeJobEntity>? children = null
    )
    {
        return new TimeJobEntity
        {
            Function = _Function,
            ExecutionTime = DateTime.UtcNow.AddHours(1),
            TenantId = tenantId,
            IsSystemJob = isSystemJob,
            Children = children ?? [],
        };
    }

    private static (
        ITimeJobManager<TimeJobEntity> Manager,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> Persistence
    ) _CreateManager(string? ambient)
    {
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(ambient);
        var dispatcher = Substitute.For<IJobsDispatcher>();
        dispatcher.IsEnabled.Returns(false);

        var manager = new JobsManager<TimeJobEntity, CronJobEntity>(
            persistence,
            Substitute.For<IJobsHostScheduler>(),
            TimeProvider.System,
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            Substitute.For<IJobsNotificationHubSender>(),
            new JobsExecutionContext(),
            dispatcher,
            Substitute.For<ICurrentCommitCoordinator>(),
            new CronScheduleCache(TimeZoneInfo.Utc),
            new SchedulerOptionsBuilder(),
            JobFunctionProvider.CreateHostRegistry(configuration: null),
            Substitute.For<ILogger<JobsManager<TimeJobEntity, CronJobEntity>>>(),
            currentTenant: tenant
        );

        return (manager, persistence);
    }

    private static void _RegisterFunction()
    {
        JobFunctionProvider.ResetForTests(discoveryComplete: false);
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [_Function] = new JobFunctionRegistration
                {
                    CronExpression = "",
                    Priority = Headless.Jobs.Enums.JobPriority.Normal,
                    Delegate = (_, _, _) => Task.CompletedTask,
                    MaxConcurrency = 0,
                },
            }
        );
        JobFunctionProvider.MarkDiscoveryComplete();
        JobFunctionProvider.Build();
    }
}
