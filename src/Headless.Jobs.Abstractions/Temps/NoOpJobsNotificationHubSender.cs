using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;

namespace Headless.Jobs.Temps;

internal class NoOpJobsNotificationHubSender : IJobsNotificationHubSender
{
    public Task AddCronJobNotifyAsync(object cronTicker)
    {
        return Task.CompletedTask;
    }

    public Task UpdateCronJobNotifyAsync(object cronTicker)
    {
        return Task.CompletedTask;
    }

    public Task RemoveCronJobNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }

    public Task AddTimeJobNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }

    public Task AddTimeJobsBatchNotifyAsync()
    {
        return Task.CompletedTask;
    }

    public Task UpdateTimeJobNotifyAsync(object timeTicker)
    {
        return Task.CompletedTask;
    }

    public Task RemoveTimeJobNotifyAsync(Guid id)
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

    public Task UpdateTimeJobFromInternalFunctionContext<TTimeJobEntity>(
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

    public Task CanceledJobNotifyAsync(Guid id)
    {
        return Task.CompletedTask;
    }
}
