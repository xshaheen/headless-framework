// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.UnitOfWork;

namespace Headless.Jobs.Managers;

// Scoped facade over the singleton JobsManager core. It resolves IUnitOfWorkManager.Current at each call and
// passes it down to the core's Add/keyed-schedule methods; Update/Delete never touched coordination and forward
// straight through. Registered scoped as both ITimeJobManager<TTimeJob> and ICronJobManager<TCronJob> so each
// resolution reads THIS scope's active unit of work, never a captive singleton reading ambient state.
internal sealed class JobsManagerFacade<TTimeJob, TCronJob>(
    JobsManager<TTimeJob, TCronJob> core,
    IUnitOfWorkManager unitOfWorkManager
) : ICronJobManager<TCronJob>, ITimeJobManager<TTimeJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly JobsManager<TTimeJob, TCronJob> _core = Argument.IsNotNull(core);
    private readonly IUnitOfWorkManager _unitOfWorkManager = Argument.IsNotNull(unitOfWorkManager);

    Task<TCronJob> ICronJobManager<TCronJob>.AddAsync(TCronJob entity, CancellationToken cancellationToken) =>
        _core.AddCronJobAsync(entity, _unitOfWorkManager.Current, cancellationToken);

    Task<TTimeJob> ITimeJobManager<TTimeJob>.AddAsync(TTimeJob entity, CancellationToken cancellationToken) =>
        _core.AddTimeJobAsync(entity, _unitOfWorkManager.Current, cancellationToken);

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
    ) => _core.AddTimeJobsBatchAsync(entities, _unitOfWorkManager.Current, cancellationToken);

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
    ) => _core.AddCronJobsBatchAsync(entities, _unitOfWorkManager.Current, cancellationToken);

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
    ) =>
        _core.ScheduleKeyedTimeJobAsync(key, entity, expectedGeneration, _unitOfWorkManager.Current, cancellationToken);

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
            TransactionEnlistment.WhenAvailable,
            _unitOfWorkManager.Current,
            cancellationToken
        );

    Task<JobScheduleResult> ITimeJobManager<TTimeJob>.CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        TransactionEnlistment enlistment,
        CancellationToken cancellationToken
    ) =>
        _core.CancelKeyedTimeJobAsync(
            scope,
            key,
            expectedGeneration,
            enlistment,
            _unitOfWorkManager.Current,
            cancellationToken
        );
}
