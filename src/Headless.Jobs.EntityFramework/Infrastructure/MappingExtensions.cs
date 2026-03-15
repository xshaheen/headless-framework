using System.Linq.Expressions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Jobs.Infrastructure;

internal static class MappingExtensions
{
    public static Expression<Func<TCronTicker, CronJobEntity>> ForCronTickerExpressions<TCronTicker>()
        where TCronTicker : CronJobEntity, new() =>
        e => new CronJobEntity
        {
            Id = e.Id,
            Expression = e.Expression,
            Function = e.Function,
            RetryIntervals = e.RetryIntervals,
            Retries = e.Retries,
        };

    internal static Expression<Func<TTimeTicker, TimeJobEntity>> ForQueueTimeTickers<TTimeTicker>()
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new() =>
        e => new TimeJobEntity
        {
            Id = e.Id,
            Function = e.Function,
            Retries = e.Retries,
            RetryIntervals = e.RetryIntervals,
            UpdatedAt = e.UpdatedAt,
            ParentId = e.ParentId,
            ExecutionTime = e.ExecutionTime,
            Children = e
                .Children.Select(ch => new TimeJobEntity
                {
                    Id = ch.Id,
                    Function = ch.Function,
                    Retries = ch.Retries,
                    RetryIntervals = ch.RetryIntervals,
                    RunCondition = ch.RunCondition,
                    Children = ch
                        .Children.Select(gch => new TimeJobEntity
                        {
                            Function = gch.Function,
                            Retries = gch.Retries,
                            RetryIntervals = gch.RetryIntervals,
                            Id = gch.Id,
                            RunCondition = gch.RunCondition,
                        })
                        .ToArray(),
                })
                .ToArray(),
        };

    internal static Expression<
        Func<TCronTickerOccurrence, CronJobOccurrenceEntity<TCronTicker>>
    > ForQueueCronTickerOccurrence<TCronTickerOccurrence, TCronTicker>()
        where TCronTicker : CronJobEntity, new()
        where TCronTickerOccurrence : CronJobOccurrenceEntity<TCronTicker>, new() =>
        e => new CronJobOccurrenceEntity<TCronTicker>
        {
            Id = e.Id,
            UpdatedAt = e.UpdatedAt,
            CronJobId = e.CronJobId,
            CronTicker = new TCronTicker
            {
                Id = e.CronTicker.Id,
                Function = e.CronTicker.Function,
                RetryIntervals = e.CronTicker.RetryIntervals,
                Retries = e.CronTicker.Retries,
            },
        };

    internal static Expression<
        Func<TCronTickerOccurrence, CronJobOccurrenceEntity<TCronTicker>>
    > ForLatestQueuedCronTickerOccurrence<TCronTickerOccurrence, TCronTicker>()
        where TCronTicker : CronJobEntity, new()
        where TCronTickerOccurrence : CronJobOccurrenceEntity<TCronTicker>, new() =>
        e => new CronJobOccurrenceEntity<TCronTicker>
        {
            Id = e.Id,
            CreatedAt = e.CreatedAt,
            CronJobId = e.CronJobId,
            ExecutionTime = e.ExecutionTime,
            CronTicker = new TCronTicker
            {
                Id = e.CronTicker.Id,
                Function = e.CronTicker.Function,
                Expression = e.CronTicker.Expression,
                RetryIntervals = e.CronTicker.RetryIntervals,
                Retries = e.CronTicker.Retries,
            },
        };

    internal static void UpdateCronTickerOccurrence<TCronTicker>(
        this UpdateSettersBuilder<CronJobOccurrenceEntity<TCronTicker>> setters,
        InternalFunctionContext functionContext
    )
        where TCronTicker : CronJobEntity, new()
    {
        var propsToUpdate = functionContext.GetPropsToUpdate();

        // STATUS / SKIPPED
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.Status))
            && functionContext.Status != JobStatus.Skipped
        )
        {
            setters.SetProperty(x => x.Status, functionContext.Status);
        }
        else
        {
            setters
                .SetProperty(x => x.Status, functionContext.Status)
                .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
        {
            setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails))
            && functionContext.Status != JobStatus.Skipped
        )
        {
            setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
        {
            setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
        {
            setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            setters.SetProperty(x => x.LockHolder, (string?)null).SetProperty(x => x.LockedAt, (DateTime?)null);
        }

        // EXECUTION TIME
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutionTime)))
        {
            setters.SetProperty(x => x.ExecutionTime, functionContext.ExecutionTime);
        }
    }

    internal static void UpdateTimeTicker<TTimeTicker>(
        this UpdateSettersBuilder<TTimeTicker> setters,
        InternalFunctionContext functionContext,
        DateTime updatedAt
    )
        where TTimeTicker : TimeJobEntity<TTimeTicker>, new()
    {
        var propsToUpdate = functionContext.GetPropsToUpdate();

        // STATUS / SKIPPED
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.Status))
            && functionContext.Status != JobStatus.Skipped
        )
        {
            setters.SetProperty(x => x.Status, functionContext.Status);
        }
        else
        {
            setters
                .SetProperty(x => x.Status, functionContext.Status)
                .SetProperty(x => x.SkippedReason, functionContext.ExceptionDetails);
        }

        // EXECUTED_AT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ExecutedAt)))
        {
            setters.SetProperty(x => x.ExecutedAt, functionContext.ExecutedAt);
        }

        // EXCEPTION DETAILS
        if (
            propsToUpdate.Contains(nameof(InternalFunctionContext.ExceptionDetails))
            && functionContext.Status != JobStatus.Skipped
        )
        {
            setters.SetProperty(x => x.ExceptionMessage, functionContext.ExceptionDetails);
        }

        // ELAPSED_TIME
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ElapsedTime)))
        {
            setters.SetProperty(x => x.ElapsedTime, functionContext.ElapsedTime);
        }

        // RETRY COUNT
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.RetryCount)))
        {
            setters.SetProperty(x => x.RetryCount, functionContext.RetryCount);
        }

        // RELEASE LOCK
        if (propsToUpdate.Contains(nameof(InternalFunctionContext.ReleaseLock)))
        {
            setters.SetProperty(x => x.LockHolder, (string?)null).SetProperty(x => x.LockedAt, (DateTime?)null);
        }

        // UPDATED_AT ALWAYS
        setters.SetProperty(x => x.UpdatedAt, updatedAt);
    }
}
