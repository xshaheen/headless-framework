// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs;

/// <summary>
/// The job managers' notification seam over the dashboard's live stream: each notification that names a row queues it
/// on <see cref="JobsChangePublisher"/>, which reads it back after the write and publishes it to this node's streams.
/// </summary>
/// <remarks>
/// The managers notify after their write returned, so the row read back is the committed one. A notification that names
/// no row, such as a batch add, publishes nothing: the change reader on every node picks those rows up. The host's
/// last exception is kept for the nodes view; the other scheduler and host-status notifications carry nothing the
/// module shows, so they are dropped.
/// </remarks>
internal sealed class JobsDashboardEventNotifier(
    IJobsDashboardBackend backend,
    JobsChangePublisher publisher,
    TimeProvider timeProvider
) : IJobsNotificationHubSender
{
    private readonly IJobsDashboardBackend _backend = Argument.IsNotNull(backend);
    private readonly JobsChangePublisher _publisher = Argument.IsNotNull(publisher);
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private JobsHostException? _lastHostException;

    /// <summary>
    /// Gets this host's last scheduler exception and when it was reported, or <see langword="null"/> when none was.
    /// </summary>
    /// <remarks>
    /// It lives in this process only and is lost on restart; peers never send theirs, so the nodes view shows it on
    /// the serving node's row alone.
    /// </remarks>
    public JobsHostException? LastHostException => Volatile.Read(ref _lastHostException);

    public Task AddCronJobNotifyAsync(object cronJob) => _Row(JobChangeKind.CronJob, cronJob, affectsAttention: true);

    public Task UpdateCronJobNotifyAsync(object cronJob) =>
        _Row(JobChangeKind.CronJob, cronJob, affectsAttention: true);

    public Task RemoveCronJobNotifyAsync(Guid id)
    {
        _publisher.PublishRemoved(JobChangeKind.CronJob, id);

        return Task.CompletedTask;
    }

    public Task AddTimeJobNotifyAsync(Guid id) => _Id(JobChangeKind.TimeJob, id, affectsAttention: false);

    public Task AddTimeJobsBatchNotifyAsync() => Task.CompletedTask;

    public Task UpdateTimeJobNotifyAsync(object timeJob) =>
        _Row(JobChangeKind.TimeJob, timeJob, affectsAttention: false);

    public Task RemoveTimeJobNotifyAsync(Guid id)
    {
        _publisher.PublishRemoved(JobChangeKind.TimeJob, id);

        return Task.CompletedTask;
    }

    public void UpdateActiveThreads(object activeThreads) { }

    public void UpdateNextOccurrence(object nextOccurrence) { }

    public void UpdateHostStatus(object active) { }

    public void UpdateHostException(object exceptionMessage)
    {
        if (exceptionMessage?.ToString() is { Length: > 0 } message)
        {
            Volatile.Write(ref _lastHostException, new JobsHostException(message, _timeProvider.GetUtcNow()));
        }
    }

    public Task AddCronOccurrenceAsync(Guid groupId, object occurrence) =>
        _Row(JobChangeKind.CronJobOccurrence, occurrence, affectsAttention: false);

    public Task UpdateCronOccurrenceAsync(Guid groupId, object occurrence) =>
        _Row(JobChangeKind.CronJobOccurrence, occurrence, affectsAttention: true);

    public Task UpdateTimeJobFromExecutionState<TTimeJobEntity>(JobExecutionState executionState)
        where TTimeJobEntity : TimeJobEntity<TTimeJobEntity>, new()
    {
        return _Id(JobChangeKind.TimeJob, executionState.JobId, _AffectsAttention(executionState));
    }

    public Task UpdateCronOccurrenceFromExecutionState<TCronJobEntity>(JobExecutionState executionState)
        where TCronJobEntity : CronJobEntity, new()
    {
        return _Id(JobChangeKind.CronJobOccurrence, executionState.JobId, _AffectsAttention(executionState));
    }

    public Task CanceledJobNotifyAsync(Guid id) => _Id(JobChangeKind.TimeJob, id, affectsAttention: false);

    public void UpdateJobProgress(JobExecutionState executionState, JobProgress progress)
    {
        _publisher.Enqueue(
            executionState.Type == JobType.CronJobOccurrence ? JobChangeKind.CronJobOccurrence : JobChangeKind.TimeJob,
            executionState.JobId,
            affectsAttention: false
        );
    }

    // A run entering or leaving a finished state can add or remove a Needs-you item; a claim or a start cannot.
    private static bool _AffectsAttention(JobExecutionState state) =>
        state.Status is not (JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress);

    private Task _Row(JobChangeKind kind, object row, bool affectsAttention)
    {
        return _backend.RowIdOf(row) is { } id ? _Id(kind, id, affectsAttention) : Task.CompletedTask;
    }

    private Task _Id(JobChangeKind kind, Guid id, bool affectsAttention)
    {
        _publisher.Enqueue(kind, id, affectsAttention);

        return Task.CompletedTask;
    }
}

/// <summary>A scheduler exception this host reported, and when.</summary>
internal sealed record JobsHostException(string Message, DateTimeOffset At);
