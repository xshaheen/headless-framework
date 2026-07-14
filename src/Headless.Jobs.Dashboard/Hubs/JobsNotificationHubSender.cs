// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Hubs;

internal sealed class JobsNotificationHubSender : IJobsNotificationHubSender, IDisposable
{
    private readonly IHubContext<JobsNotificationHub> _hubContext;
    private readonly ILogger<JobsNotificationHubSender> _logger;
    private readonly Timer _timeJobUpdateTimer;
    private int _hasPendingTimeJobUpdate;
    private static readonly TimeSpan _TimeJobUpdateDebounce = TimeSpan.FromMilliseconds(100);

    public JobsNotificationHubSender(
        IHubContext<JobsNotificationHub> hubContext,
        ILogger<JobsNotificationHubSender> logger
    )
    {
        _hubContext = Argument.IsNotNull(hubContext);
        _logger = Argument.IsNotNull(logger);

        _timeJobUpdateTimer = new Timer(
            _TimeJobUpdateCallback,
            state: null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
    }

    public async Task AddCronJobNotifyAsync(object cronJob)
    {
        await _hubContext.Clients.All.SendAsync("AddCronJobNotification", cronJob).ConfigureAwait(false);
    }

    public async Task UpdateCronJobNotifyAsync(object cronJob)
    {
        await _hubContext.Clients.All.SendAsync("UpdateCronJobNotification", cronJob).ConfigureAwait(false);
    }

    public async Task RemoveCronJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("RemoveCronJobNotification", id).ConfigureAwait(false);
    }

    public async Task AddTimeJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("AddTimeJobNotification", id).ConfigureAwait(false);
    }

    public async Task AddTimeJobsBatchNotifyAsync()
    {
        await _hubContext.Clients.All.SendAsync("AddTimeJobsBatchNotification").ConfigureAwait(false);
    }

    public async Task UpdateTimeJobNotifyAsync(object timeJob)
    {
        await _hubContext.Clients.All.SendAsync("UpdateTimeJobNotification", timeJob).ConfigureAwait(false);
    }

    public async Task RemoveTimeJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("RemoveTimeJobNotification", id).ConfigureAwait(false);
    }

    public void UpdateActiveThreads(object activeThreads)
    {
        _ObserveSend(
            _hubContext.Clients.All.SendAsync("GetActiveThreadsNotification", activeThreads),
            "GetActiveThreadsNotification"
        );
    }

    public void UpdateNextOccurrence(object? nextOccurrence)
    {
        if (nextOccurrence != null)
        {
            _ObserveSend(
                _hubContext.Clients.All.SendAsync("GetNextOccurrenceNotification", nextOccurrence),
                "GetNextOccurrenceNotification"
            );
        }
    }

    public void UpdateHostStatus(object active)
    {
        _ObserveSend(
            _hubContext.Clients.All.SendAsync("GetHostStatusNotification", active),
            "GetHostStatusNotification"
        );
    }

    public void UpdateHostException(object exceptionMessage)
    {
        _ObserveSend(
            _hubContext.Clients.All.SendAsync("UpdateHostExceptionNotification", exceptionMessage),
            "UpdateHostExceptionNotification"
        );
    }

    public async Task UpdateNodesAsync(object nodes)
    {
        await _hubContext.Clients.All.SendAsync("UpdateNodesNotification", nodes).ConfigureAwait(false);
    }

    public async Task AddCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        await _hubContext
            .Clients.Group(groupId.ToString())
            .SendAsync("AddCronOccurrenceNotification", occurrence)
            .ConfigureAwait(false);
    }

    public async Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence)
    {
        await _hubContext
            .Clients.Group(groupId.ToString())
            .SendAsync("UpdateCronOccurrenceNotification", occurrence)
            .ConfigureAwait(false);
    }

    public Task UpdateTimeJobFromExecutionState<TTimeJob>(JobExecutionState executionState)
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

        _ObserveSend(_hubContext.Clients.All.SendAsync("UpdateTimeJobNotification"), "UpdateTimeJobNotification");
    }

    public Task UpdateCronOccurrenceFromExecutionState<TCronJob>(JobExecutionState executionState)
        where TCronJob : CronJobEntity, new()
    {
        var updatePayload = new
        {
            id = executionState.JobId,
            status = executionState.Status,
            cronJobId = executionState.ParentId,
            executedAt = executionState.ExecutedAt,
            elapsedTime = executionState.ElapsedTime,
            retryCount = executionState.RetryCount,
            exceptionMessage = executionState.ExceptionDetails,
        };

        _ObserveSend(
            _hubContext
                .Clients.Group(executionState.ParentId?.ToString() ?? string.Empty)
                .SendAsync("UpdateCronOccurrenceNotification", updatePayload),
            "UpdateCronOccurrenceNotification"
        );

        return Task.CompletedTask;
    }

    public async Task CanceledJobNotifyAsync(Guid id)
    {
        await _hubContext.Clients.All.SendAsync("CanceledJobNotification", id).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _timeJobUpdateTimer.Dispose();
    }

    private void _ObserveSend(Task sendTask, string methodName)
    {
        _ = sendTask.ContinueWith(
            static (faultedTask, state) =>
            {
                var observer = (SendObserver)state!;
                var exception = faultedTask.Exception?.GetBaseException();

                if (exception is not null)
                {
                    observer.Logger.SignalRNotificationSendFailed(exception, observer.MethodName);
                }
            },
            new SendObserver(_logger, methodName),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private readonly record struct SendObserver(ILogger Logger, string MethodName);
}

internal static partial class JobsNotificationHubSenderLogs
{
    [LoggerMessage(
        EventId = 2010,
        EventName = "JobsDashboardSignalRNotificationSendFailed",
        Level = LogLevel.Warning,
        Message = "Jobs dashboard SignalR notification {MethodName} failed."
    )]
    public static partial void SignalRNotificationSendFailed(
        this ILogger logger,
        Exception exception,
        string methodName
    );
}
