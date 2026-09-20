// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Managers;
using Headless.UnitOfWork;

namespace Headless.Jobs;

/// <summary>
/// The enlisted scheduling surface behind <c>unit.Jobs</c>: binds the singleton core to the caller's unit through
/// a facade that passes it into every coordinated write. A singleton that holds no unit of its own; the bound
/// receivers are created once per unit and kept as unit-local state, so repeated reads allocate nothing.
/// </summary>
internal sealed class UnitOfWorkJobsFeature<TTimeJob, TCronJob>(
    JobsManager<TTimeJob, TCronJob> core,
    JobFunctionRegistry functionRegistry,
    IInternalJobManager internalJobManager,
    IJobsHostScheduler jobsHostScheduler,
    JobsRequestSerializationOptions serializationOptions,
    TimeProvider timeProvider,
    JobSchedulingPolicies policies,
    SchedulerOptionsBuilder? schedulerOptions = null
) : IUnitOfWorkJobs
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public IJobScheduler Bind(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        // Resolve the facade outside the scheduler's factory so the two GetOrAdd calls never nest.
        var facade = _Facade(unitOfWork);

        return unitOfWork.GetOrAdd(
            (Feature: this, Facade: facade),
            static (_, state) => state.Feature._CreateScheduler(state.Facade)
        );
    }

    private JobScheduler<TTimeJob, TCronJob> _CreateScheduler(JobsManagerFacade<TTimeJob, TCronJob> facade) =>
        new(
            facade,
            facade,
            functionRegistry,
            internalJobManager,
            jobsHostScheduler,
            serializationOptions,
            timeProvider,
            policies,
            schedulerOptions
        );

    public ITimeJobManager<TRequested> BindTimeJobs<TRequested>(IUnitOfWork unitOfWork)
        where TRequested : TimeJobEntity<TRequested>, new()
    {
        Argument.IsNotNull(unitOfWork);

        return _Facade(unitOfWork) as ITimeJobManager<TRequested>
            ?? throw new InvalidOperationException(
                $"The Jobs host is registered with time-job entity '{typeof(TTimeJob).Name}', not '{typeof(TRequested).Name}'."
            );
    }

    public ICronJobManager<TRequested> BindCronJobs<TRequested>(IUnitOfWork unitOfWork)
        where TRequested : CronJobEntity, new()
    {
        Argument.IsNotNull(unitOfWork);

        return _Facade(unitOfWork) as ICronJobManager<TRequested>
            ?? throw new InvalidOperationException(
                $"The Jobs host is registered with cron-job entity '{typeof(TCronJob).Name}', not '{typeof(TRequested).Name}'."
            );
    }

    // One facade per unit serves all three receivers: it implements both manager interfaces and the scheduler
    // wraps it.
    private JobsManagerFacade<TTimeJob, TCronJob> _Facade(IUnitOfWork unitOfWork) =>
        unitOfWork.GetOrAdd(core, static (unit, core) => new JobsManagerFacade<TTimeJob, TCronJob>(core, unit));
}
