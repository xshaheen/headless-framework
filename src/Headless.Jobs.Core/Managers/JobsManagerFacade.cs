// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.UnitOfWork;

namespace Headless.Jobs.Managers;

// Facade over the singleton JobsManager core that fixes the unit of work its Add/keyed-schedule writes enlist in:
// null for the autonomous receiver registered in DI, the bound unit for the receivers behind unit.Jobs. The unit
// is captured at construction, never read from ambient state. Update/Delete never touched coordination and
// forward straight through.
internal sealed class JobsManagerFacade<TTimeJob, TCronJob>(
    JobsManager<TTimeJob, TCronJob> core,
    IUnitOfWork? unitOfWork = null
) : ICronJobManager<TCronJob>, ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly JobsManager<TTimeJob, TCronJob> _core = Argument.IsNotNull(core);

    Task<TCronJob> ICronJobManager<TCronJob>.AddAsync(TCronJob entity, CancellationToken cancellationToken) =>
        _core.AddCronJobAsync(entity, unitOfWork, cancellationToken);

    Task<TTimeJob> ITimeJobManager<TTimeJob>.AddAsync(TTimeJob entity, CancellationToken cancellationToken) =>
        _core.AddTimeJobAsync(entity, unitOfWork, cancellationToken);

    Task<TTimeJob> ITimeJobManager<TTimeJob>.AddIdempotentAsync(
        TTimeJob entity,
        string idempotencyKey,
        TimeSpan idempotencyTtl,
        CancellationToken cancellationToken
    ) => _core.AddIdempotentTimeJobAsync(entity, idempotencyKey, idempotencyTtl, unitOfWork, cancellationToken);

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.UpdateAsync(
        TCronJob cronJob,
        CancellationToken cancellationToken
    ) => _core.UpdateCronJobAsync(cronJob, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.UpdateAsync(
        TTimeJob timeJob,
        CancellationToken cancellationToken
    ) => _core.UpdateTimeJobAsync(timeJob, cancellationToken);

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _core.DeleteCronJobAsync(id, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        _core.DeleteTimeJobAsync(id, cancellationToken);

    Task<List<TTimeJob>> ITimeJobManager<TTimeJob>.AddBatchAsync(
        List<TTimeJob> entities,
        CancellationToken cancellationToken
    ) => _core.AddTimeJobsBatchAsync(entities, unitOfWork, cancellationToken);

    Task<JobResult<List<TTimeJob>>> ITimeJobManager<TTimeJob>.UpdateBatchAsync(
        List<TTimeJob> timeJobs,
        CancellationToken cancellationToken
    ) => _core.UpdateTimeJobsBatchAsync(timeJobs, cancellationToken);

    Task<JobResult<TTimeJob>> ITimeJobManager<TTimeJob>.DeleteBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken
    ) => _core.DeleteTimeJobsBatchAsync(ids, cancellationToken);

    Task<List<TCronJob>> ICronJobManager<TCronJob>.AddBatchAsync(
        List<TCronJob> entities,
        CancellationToken cancellationToken
    ) => _core.AddCronJobsBatchAsync(entities, unitOfWork, cancellationToken);

    Task<JobResult<List<TCronJob>>> ICronJobManager<TCronJob>.UpdateBatchAsync(
        List<TCronJob> cronJobs,
        CancellationToken cancellationToken
    ) => _core.UpdateCronJobsBatchAsync(cronJobs, cancellationToken);

    Task<JobResult<TCronJob>> ICronJobManager<TCronJob>.DeleteBatchAsync(
        List<Guid> ids,
        CancellationToken cancellationToken
    ) => _core.DeleteCronJobsBatchAsync(ids, cancellationToken);

    Task<JobScheduleResult> ITimeJobManager<TTimeJob>.ScheduleKeyedAsync(
        JobKey key,
        TTimeJob entity,
        long? expectedGeneration,
        CancellationToken cancellationToken
    ) => _core.ScheduleKeyedTimeJobAsync(key, entity, expectedGeneration, unitOfWork, cancellationToken);

    Task<JobScheduleResult> ITimeJobManager<TTimeJob>.CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken
    ) =>
        _core.CancelKeyedTimeJobAsync(
            scope,
            key,
            expectedGeneration,
            TransactionEnlistment.Optional,
            unitOfWork,
            cancellationToken
        );

    Task<JobScheduleResult> ITimeJobManager<TTimeJob>.CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        TransactionEnlistment enlistment,
        CancellationToken cancellationToken
    ) => _core.CancelKeyedTimeJobAsync(scope, key, expectedGeneration, enlistment, unitOfWork, cancellationToken);
}
