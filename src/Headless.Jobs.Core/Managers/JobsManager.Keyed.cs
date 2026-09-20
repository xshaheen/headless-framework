// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Headless.Jobs.MultiTenancy;
using Headless.UnitOfWork;

namespace Headless.Jobs.Managers;

internal sealed partial class JobsManager<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    // Called only by JobsManagerFacade, which passes the unit it was bound with (null for the autonomous receiver).
    internal async Task<JobScheduleResult> ScheduleKeyedTimeJobAsync(
        JobKey key,
        TTimeJob entity,
        long? expectedGeneration,
        IUnitOfWork? unitOfWork,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(key);
        Argument.IsNotNull(entity);
        Argument.IsPositive(expectedGeneration);
        JobIntentFingerprint.RejectOrdinaryMutation(entity);
        JobIntentFingerprint.Validate(entity);
        var coordinated = _TryCaptureCoordinatedContext(
            unitOfWork,
            entity.Enlistment,
            entity.Function,
            requireSavepoints: true
        );
        var now = timeProvider.GetUtcNow();
        _StampTimeJobTree(entity, now, assignIds: true);
        await _RunSchedulePipelineAsync(entity, cancellationToken).ConfigureAwait(false);
        _StampTimeJobTree(entity, now, assignIds: false);
        _ResolveChainTenants(entity);
        if (!_functionRegistry.Functions.ContainsKey(entity.Function))
        {
            throw new Exceptions.JobValidatorException($"Cannot find JobFunction with name {entity.Function}");
        }
        JobIntentFingerprint.Normalize(entity);
        JobScheduleResult result;
        if (coordinated is { } context)
        {
            _PrepareCoordinatedWrite(context);
            result = await context
                .Writer.WriteKeyedTimeJobAsync(key, entity, expectedGeneration, context.Relational, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            result = await persistenceProvider
                .ScheduleKeyedTimeJobAsync(key, entity, expectedGeneration, cancellationToken)
                .ConfigureAwait(false);
        }
        return _CompleteKeyedOperation(result, coordinated);
    }

    // Called only by JobsManagerFacade, which passes the unit it was bound with (null for the autonomous receiver).
    internal Task<JobScheduleResult> CancelKeyedTimeJobAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        TransactionEnlistment enlistment,
        IUnitOfWork? unitOfWork,
        CancellationToken cancellationToken
    ) => _CancelKeyedAsync(scope, key, expectedGeneration, enlistment, unitOfWork, cancellationToken);

    private async Task<JobScheduleResult> _CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        TransactionEnlistment enlistment,
        IUnitOfWork? unitOfWork,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(scope);
        Argument.IsNotNull(key);
        Argument.IsPositive(expectedGeneration);
        var coordinated = _TryCaptureCoordinatedContext(
            unitOfWork,
            enlistment,
            scope.Function,
            requireSavepoints: true
        );
        if (scope.TenantId is null)
        {
            JobTenantValidation.ValidateSystemJob(
                explicitTenantId: null,
                ambientPresent: !string.IsNullOrWhiteSpace(_currentTenant?.Id)
            );
        }
        else
        {
            JobTenantValidation.CheckCrossTenant(scope.TenantId, _currentTenant?.Id, _rejectCrossTenant);
        }

        JobScheduleResult result;
        if (coordinated is { } context)
        {
            _PrepareCoordinatedWrite(context);
            result = await context
                .Writer.CancelKeyedTimeJobAsync(scope, key, expectedGeneration, context.Relational, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            result = await persistenceProvider
                .CancelKeyedTimeJobAsync(scope, key, expectedGeneration, cancellationToken)
                .ConfigureAwait(false);
        }
        return _CompleteKeyedOperation(result, coordinated);
    }

    private JobScheduleResult _CompleteKeyedOperation(JobScheduleResult result, CoordinatedJobContext? coordinated)
    {
        if (
            result.Disposition
            is JobScheduleDisposition.Created
                or JobScheduleDisposition.Replaced
                or JobScheduleDisposition.Cancelled
                or JobScheduleDisposition.CancellationRequested
        )
        {
            if (coordinated is { } context)
            {
                _SignalOnCommit(context.UnitOfWork, new ScheduleChangedSignal(this, result.RunId.ToString()!));
            }
            else
            {
                _jobsHostScheduler.Restart();
            }
        }
        return result with { IsProvisional = coordinated is not null };
    }
}
