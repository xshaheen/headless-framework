// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Jobs.MultiTenancy;

namespace Headless.Jobs.Managers;

internal sealed partial class JobsManager<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    async Task<JobScheduleResult> ITimeJobManager<TTimeJob>.ScheduleKeyedAsync(
        JobKey key,
        TTimeJob entity,
        long? expectedGeneration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGeneration ?? 1, nameof(expectedGeneration));
        JobIntentFingerprint.RejectOrdinaryMutation(entity);
        JobIntentFingerprint.Validate(entity);
        var coordinated = _TryCaptureCoordinatedContext(entity.RequireAtomicEnlistment, requireSavepoints: true);
        var now = timeProvider.GetUtcNow();
        _StampTimeJobTree(entity, now, assignIds: true);
        await _RunSchedulePipelineAsync(entity, cancellationToken).ConfigureAwait(false);
        _StampTimeJobTree(entity, now, assignIds: false);
        _ResolveChainTenants(entity);
        if (!_functionRegistry.Functions.ContainsKey(entity.Function))
        {
            throw new Headless.Jobs.Exceptions.JobValidatorException(
                $"Cannot find JobFunction with name {entity.Function}"
            );
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

    Task<JobScheduleResult> ITimeJobManager<TTimeJob>.CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken
    ) => _CancelKeyedAsync(scope, key, expectedGeneration, requireAtomicEnlistment: false, cancellationToken);

    Task<JobScheduleResult> ITimeJobManager<TTimeJob>.CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        bool requireAtomicEnlistment,
        CancellationToken cancellationToken
    ) => _CancelKeyedAsync(scope, key, expectedGeneration, requireAtomicEnlistment, cancellationToken);

    private async Task<JobScheduleResult> _CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        bool requireAtomicEnlistment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGeneration);
        var coordinated = _TryCaptureCoordinatedContext(requireAtomicEnlistment, requireSavepoints: true);
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
                _DeferSideEffects(
                    context.Coordinator,
                    result.RunId.ToString()!,
                    cancellationToken =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _jobsHostScheduler.Restart();
                        return Task.CompletedTask;
                    }
                );
            }
            else
            {
                _jobsHostScheduler.Restart();
            }
        }
        return result with { IsProvisional = coordinated is not null };
    }
}
