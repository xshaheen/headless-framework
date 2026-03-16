using Headless.Jobs.Entities;
using Headless.Jobs.Models;

namespace Headless.Jobs.Interfaces;

internal interface IJobsNotificationHubSender
{
    Task AddCronJobNotifyAsync(object cronJob);
    Task UpdateCronJobNotifyAsync(object cronJob);
    Task RemoveCronJobNotifyAsync(Guid id);
    Task AddTimeJobNotifyAsync(Guid id);
    Task AddTimeJobsBatchNotifyAsync();
    Task UpdateTimeJobNotifyAsync(object timeJob);
    Task RemoveTimeJobNotifyAsync(Guid id);
    void UpdateActiveThreads(object activeThreads);
    void UpdateNextOccurrence(object nextOccurrence);
    void UpdateHostStatus(object active);
    void UpdateHostException(object exceptionMessage);
    Task UpdateNodeHeartBeatAsync(object nodeHeartBeat);
    Task AddCronOccurrenceAsync(Guid groupId, object occurrence);
    Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence);
    Task UpdateTimeJobFromInternalFunctionContext<TTimeJobEntity>(InternalFunctionContext internalFunctionContext)
        where TTimeJobEntity : TimeJobEntity<TTimeJobEntity>, new();
    Task UpdateCronOccurrenceFromInternalFunctionContext<TCronJobEntity>(
        InternalFunctionContext internalFunctionContext
    )
        where TCronJobEntity : CronJobEntity, new();
    Task CanceledJobNotifyAsync(Guid id);
}
