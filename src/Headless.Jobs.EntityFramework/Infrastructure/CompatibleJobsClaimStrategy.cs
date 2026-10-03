// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable MA0133 // EF must keep DateTime.UtcNow in expression trees so providers translate the database clock before the DateTimeOffset assignment.
namespace Headless.Jobs.Infrastructure;

internal sealed partial class CompatibleJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>(
    IDbContextFactory<TDbContext> dbContextFactory,
    IJobsClaimStrategy<TTimeJob, TCronJob> nativeStrategy,
    IJobsClaimStrategy<TTimeJob, TCronJob> casStrategy,
    ILogger<CompatibleJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>>? logger = null
) : IJobsClaimStrategy<TTimeJob, TCronJob>
    where TDbContext : DbContext
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly Lock _compatibilityLock = new();

    private readonly ILogger _logger =
        logger ?? NullLogger<CompatibleJobsClaimStrategy<TDbContext, TTimeJob, TCronJob>>.Instance;

    private IJobsClaimStrategy<TTimeJob, TCronJob>? _selectedStrategy;

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimeJobsAsync(
        TimeJobEntity[] timeJobs,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (var job in strategy.ClaimTimeJobsAsync(timeJobs, cancellationToken).ConfigureAwait(false))
        {
            yield return job;
        }
    }

    public async IAsyncEnumerable<TimeJobEntity> ClaimTimedOutTimeJobsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (var job in strategy.ClaimTimedOutTimeJobsAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return job;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimCronJobOccurrencesAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) cronJobOccurrences,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (
            var occurrence in strategy
                .ClaimCronJobOccurrencesAsync(cronJobOccurrences, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            yield return occurrence;
        }
    }

    public async IAsyncEnumerable<CronJobOccurrenceEntity<TCronJob>> ClaimTimedOutCronJobOccurrencesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var strategy = _GetStrategy();
        await foreach (
            var occurrence in strategy.ClaimTimedOutCronJobOccurrencesAsync(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return occurrence;
        }
    }

    private IJobsClaimStrategy<TTimeJob, TCronJob> _GetStrategy()
    {
        lock (_compatibilityLock)
        {
            if (_selectedStrategy is not null)
            {
                return _selectedStrategy;
            }

            using var dbContext = dbContextFactory.CreateDbContext();
            var incompatibility = NativeJobsClaimCompatibility.FindIncompatibility<TTimeJob, TCronJob>(dbContext.Model);

            if (incompatibility is null)
            {
                return _selectedStrategy = nativeStrategy;
            }

            LogCasFallback(_logger, typeof(TDbContext).Name, incompatibility);
            return _selectedStrategy = casStrategy;
        }
    }

    [LoggerMessage(
        EventId = 20101,
        Level = LogLevel.Warning,
        Message = "Native Jobs claiming is incompatible with DbContext {DbContext}; using EF CAS claiming instead. Reason: {Reason}"
    )]
    private static partial void LogCasFallback(ILogger logger, string dbContext, string reason);
}
