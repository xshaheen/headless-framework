using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

internal interface IJobsNotificationHubSender
{
    Task AddCronTickerNotifyAsync(object cronTicker);
    Task UpdateCronTickerNotifyAsync(object cronTicker);
    Task RemoveCronTickerNotifyAsync(Guid id);
    Task AddTimeTickerNotifyAsync(Guid id);
    Task AddTimeTickersBatchNotifyAsync();
    Task UpdateTimeTickerNotifyAsync(object timeTicker);
    Task RemoveTimeTickerNotifyAsync(Guid id);
    void UpdateActiveThreads(object activeThreads);
    void UpdateNextOccurrence(object nextOccurrence);
    void UpdateHostStatus(object active);
    void UpdateHostException(object exceptionMessage);
    Task UpdateNodeHeartBeatAsync(object nodeHeartBeat);
    Task AddCronOccurrenceAsync(Guid groupId, object occurrence);
    Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence);
    Task UpdateTimeTickerFromInternalFunctionContext<TTimeJobEntity>(InternalFunctionContext internalFunctionContext)
        where TTimeJobEntity : TimeJobEntity<TTimeJobEntity>, new();
    Task UpdateCronOccurrenceFromInternalFunctionContext<TCronJobEntity>(
        InternalFunctionContext internalFunctionContext
    )
        where TCronJobEntity : CronJobEntity, new();
    Task CanceledTickerNotifyAsync(Guid id);
}
