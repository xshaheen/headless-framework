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
        _RejectKeyedCoordination();
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
        var result = await persistenceProvider
            .ScheduleKeyedTimeJobAsync(key, entity, expectedGeneration, cancellationToken)
            .ConfigureAwait(false);
        if (result.Disposition is JobScheduleDisposition.Created or JobScheduleDisposition.Replaced)
        {
            // Polling is the durable dispatch authority. Avoid an immediate dispatch against a superseded candidate.
            _jobsHostScheduler.Restart();
        }

        return result;
    }

    async Task<JobScheduleResult> ITimeJobManager<TTimeJob>.CancelKeyedAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        _RejectKeyedCoordination();
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

        var result = await persistenceProvider
            .CancelKeyedTimeJobAsync(scope, key, expectedGeneration, cancellationToken)
            .ConfigureAwait(false);
        if (result.Disposition is JobScheduleDisposition.Cancelled or JobScheduleDisposition.CancellationRequested)
        {
            _jobsHostScheduler.Restart();
        }

        return result;
    }

    private void _RejectKeyedCoordination()
    {
        if (_TryCaptureCoordinatedContext() is not null)
        {
            throw new NotSupportedException(
                "Keyed Jobs writes cannot yet enlist in an ambient relational transaction. No direct fallback is performed."
            );
        }
    }
}
