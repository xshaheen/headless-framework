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
