// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Managers;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.MultiTenancy;

/// <summary>
/// A keyed schedule runs the schedule pipeline on the caller's entity before the store answers. When the call fails,
/// the entity must come back with the tenant the caller passed: the tenancy middleware captures the ambient tenant
/// onto it, and a retry under another ambient tenant would otherwise read that capture as an explicit tenant.
/// </summary>
public sealed class KeyedScheduleTenantRestoreTests : TestBase
{
    private const string _Function = "keyed-tenant-fn";
    private const string _CapturedTenant = "t-captured";

    private readonly List<JobsPostCommitSignalService> _workers = [];

    protected override ValueTask DisposeAsyncCore()
    {
        foreach (var worker in _workers)
        {
            worker.Dispose();
        }

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task a_failed_keyed_schedule_restores_the_tenant_the_pipeline_captured()
    {
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        persistence
            .ScheduleKeyedTimeJobAsync(
                Arg.Any<JobKey>(),
                Arg.Any<TimeJobEntity>(),
                Arg.Any<long?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<JobScheduleResult>>(_ => throw new InvalidOperationException("store unavailable"));
        var manager = _CreateManager(persistence);
        var entity = _Job();

        var act = () =>
            ((ITimeJobManager<TimeJobEntity>)manager).ScheduleKeyedAsync(
                new JobKey("invoice-1"),
                entity,
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        entity.TenantId.Should().BeNull("the caller passed no tenant, so a retry must resolve it afresh");
    }

    [Fact]
    public async Task a_successful_keyed_schedule_keeps_the_tenant_the_pipeline_captured()
    {
        var persistence = Substitute.For<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        persistence
            .ScheduleKeyedTimeJobAsync(
                Arg.Any<JobKey>(),
                Arg.Any<TimeJobEntity>(),
                Arg.Any<long?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new JobScheduleResult(
                JobScheduleDisposition.Created,
                call.ArgAt<TimeJobEntity>(1).Id,
                Generation: 1,
                JobStatus.Idle
            ));
        var manager = _CreateManager(persistence);
        var entity = _Job();

        await ((ITimeJobManager<TimeJobEntity>)manager).ScheduleKeyedAsync(
            new JobKey("invoice-2"),
            entity,
            cancellationToken: AbortToken
        );

        entity.TenantId.Should().Be(_CapturedTenant);
    }

    private JobsManagerFacade<TimeJobEntity, CronJobEntity> _CreateManager(
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity> persistence
    )
    {
        var dispatcher = Substitute.For<IJobsDispatcher>();
        dispatcher.IsEnabled.Returns(false);
        var signals = new JobsPostCommitSignalService(
            TestActivationBarrier.Opened(),
            TimeProvider.System,
            Substitute.For<ILogger<JobsPostCommitSignalService>>()
        );
        _workers.Add(signals);

        var manager = new JobsManager<TimeJobEntity, CronJobEntity>(
            persistence,
            Substitute.For<IJobsHostScheduler>(),
            TimeProvider.System,
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            Substitute.For<IJobsNotificationHubSender>(),
            new JobsExecutionContext(),
            dispatcher,
            new CronScheduleCache(TimeZoneInfo.Utc),
            signals,
            _Registry(),
            Substitute.For<ILogger<JobsManager<TimeJobEntity, CronJobEntity>>>()
        );

        return new JobsManagerFacade<TimeJobEntity, CronJobEntity>(manager);
    }

    // Stands in for the tenancy middleware: it captures a tenant onto the entity, as an ambient capture does.
    private static JobFunctionRegistry _Registry() =>
        JobFunctionRegistryBuilder.Build(
            [
                new KeyValuePair<string, JobFunctionRegistration>(
                    _Function,
                    new JobFunctionRegistration
                    {
                        CronExpression = "",
                        Priority = JobPriority.Normal,
                        Delegate = (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                    }
                ),
            ],
            [],
            []
        ) with
        {
            Middleware = JobMiddlewarePipeline.Create(
                [
                    new JobScheduleMiddlewareRegistration(
                        "test:capture-tenant",
                        Function: null,
                        Priority: 0,
                        (context, next, cancellationToken) =>
                        {
                            context.Job.TenantId ??= _CapturedTenant;
                            return next(cancellationToken);
                        }
                    ),
                ],
                []
            ),
        };

    private static TimeJobEntity _Job() =>
        new()
        {
            Function = _Function,
            ContractVersion = "1",
            ExecutionTime = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Request = [1],
        };
}
