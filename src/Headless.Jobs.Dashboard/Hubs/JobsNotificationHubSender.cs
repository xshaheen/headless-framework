using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.AspNetCore.SignalR;

namespace Headless.Jobs.Hubs;

internal sealed class JobsNotificationHubSender : IJobsNotificationHubSender, IDisposable
{
    private readonly IHubContext<JobsNotificationHub> _hubContext;
    private readonly Timer _timeJobUpdateTimer;
    private int _hasPendingTimeJobUpdate;
    private static readonly TimeSpan _TimeJobUpdateDebounce = TimeSpan.FromMilliseconds(100);

    public JobsNotificationHubSender(IHubContext<JobsNotificationHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _timeJobUpdateTimer = new Timer(
            _TimeJobUpdateCallback,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
    }

    public async Task AddCronJobNotifyAsync(object cronJob)
    {
        await _hubContext.Clients.All.SendAsync("AddCronJobNotification", cronJob);
    }

    public async Task UpdateCronJobNotifyAsync(object cronJob)
    {
        await _hubContext.Clients.All.SendAsync("UpdateCronJobNotification", cronJob);
    }

    public async Task RemoveCronJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("RemoveCronJobNotification", id);
    }

    public async Task AddTimeJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("AddTimeJobNotification", id);
    }

    public async Task AddTimeJobsBatchNotifyAsync()
    {
        await _hubContext.Clients.All.SendAsync("AddTimeJobsBatchNotification");
    }

    public async Task UpdateTimeJobNotifyAsync(object timeJob)
    {
        await _hubContext.Clients.All.SendAsync("UpdateTimeJobNotification", timeJob);
    }

    public async Task RemoveTimeJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("RemoveTimeJobNotification", id);
    }

    public void UpdateActiveThreads(object activeThreads)
    {
        _ = _hubContext.Clients.All.SendAsync("GetActiveThreadsNotification", activeThreads);
    }

    public void UpdateNextOccurrence(object nextOccurrence)
    {
        if (nextOccurrence != null)
        {
            _ = _hubContext.Clients.All.SendAsync("GetNextOccurrenceNotification", nextOccurrence);
        }
    }

    public void UpdateHostStatus(object active)
    {
        _ = _hubContext.Clients.All.SendAsync("GetHostStatusNotification", active);
    }

    public void UpdateHostException(object exceptionMessage)
    {
        _ = _hubContext.Clients.All.SendAsync("UpdateHostExceptionNotification", exceptionMessage);
    }

    public async Task UpdateNodeHeartBeatAsync(object nodeHeartBeat)
    {
        await _hubContext.Clients.All.SendAsync("UpdateNodeHeartBeat", nodeHeartBeat);
    }

    public async Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        await _hubContext.Clients.Group(groupId.ToString()).SendAsync("AddCronOccurrenceNotification", occurrence);
    }

    public async Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        await _hubContext.Clients.Group(groupId.ToString()).SendAsync("UpdateCronOccurrenceNotification", occurrence);
    }

    public Task UpdateTimeJobFromInternalFunctionContext<TTimeJob>(
        InternalFunctionContext internalFunctionContext
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
    {
        // Debounce high-frequency updates into a single notification
        if (Interlocked.Exchange(ref _hasPendingTimeJobUpdate, 1) == 0)
        {
            _timeJobUpdateTimer.Change(_TimeJobUpdateDebounce, Timeout.InfiniteTimeSpan);
        }

        return Task.CompletedTask;
    }

    private void _TimeJobUpdateCallback(object? _)
    {
        if (Interlocked.Exchange(ref _hasPendingTimeJobUpdate, 0) == 0)
        {
            return;
        }

        var __ = _hubContext.Clients.All.SendAsync("UpdateTimeJobNotification");
    }

    public Task UpdateCronOccurrenceFromInternalFunctionContext<TCronJob>(
        InternalFunctionContext internalFunctionContext
    )
        where TCronJob : CronJobEntity, new()
    {
        var updatePayload = new
        {
            id = internalFunctionContext.JobId,
            status = internalFunctionContext.Status,
            cronJobId = internalFunctionContext.ParentId,
            executedAt = internalFunctionContext.ExecutedAt,
            elapsedTime = internalFunctionContext.ElapsedTime,
            retryCount = internalFunctionContext.RetryCount,
            exceptionMessage = internalFunctionContext.ExceptionDetails,
        };

        _ = _hubContext
            .Clients.Group(internalFunctionContext.ParentId?.ToString() ?? string.Empty)
            .SendAsync("UpdateCronOccurrenceNotification", updatePayload);

        return Task.CompletedTask;
    }

    public async Task CanceledJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("CanceledJobNotification", id);
    }

    public void Dispose()
    {
        _timeJobUpdateTimer.Dispose();
    }
}
