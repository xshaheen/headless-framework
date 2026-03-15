using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

namespace Headless.Jobs.Temps;

internal class NoOpJobsNotificationHubSender : IJobsNotificationHubSender
{
    public Task AddCronTickerNotifyAsync(object cronTicker)
    {
        return Task.CompletedTask;
    }

    public Task UpdateCronTickerNotifyAsync(object cronTicker)
    {
        return Task.CompletedTask;
    }

    public Task RemoveCronTickerNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }

    public Task AddTimeTickerNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }

    public Task AddTimeTickersBatchNotifyAsync()
    {
        return Task.CompletedTask;
    }

    public Task UpdateTimeTickerNotifyAsync(object timeTicker)
    {
        return Task.CompletedTask;
    }

    public Task RemoveTimeTickerNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }

    public void UpdateActiveThreads(object activeThreads) { }

    public void UpdateNextOccurrence(object nextOccurrence) { }

    public void UpdateHostStatus(object active) { }

    public void UpdateHostException(object exceptionMessage) { }

    public Task UpdateNodeHeartBeatAsync(object nodeHeartBeat)
    {
        return Task.CompletedTask;
    }

    public Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        return Task.CompletedTask;
    }

    public Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        return Task.CompletedTask;
    }

    public Task UpdateTimeTickerFromInternalFunctionContext<TTimeJobEntity>(
        InternalFunctionContext internalFunctionContext
    )
        where TTimeJobEntity : TimeJobEntity<TTimeJobEntity>, new()
    {
        return Task.CompletedTask;
    }

    public Task UpdateCronOccurrenceFromInternalFunctionContext<TCronJobEntity>(
        InternalFunctionContext internalFunctionContext
    )
        where TCronJobEntity : CronJobEntity, new()
    {
        return Task.CompletedTask;
    }

    public Task CanceledTickerNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }
}
