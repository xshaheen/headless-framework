using Headless.Jobs.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs.Base;

public class JobFunctionContext<TRequest> : JobFunctionContext
{
    public JobFunctionContext(JobFunctionContext jobFunctionContext, TRequest request)
    {
        Request = request;
        Id = jobFunctionContext.Id;
        Type = jobFunctionContext.Type;
        RetryCount = jobFunctionContext.RetryCount;
        IsDue = jobFunctionContext.IsDue;
        ScheduledFor = jobFunctionContext.ScheduledFor;
        RequestCancelOperationAction = jobFunctionContext.RequestCancelOperationAction;
        CronOccurrenceOperations = jobFunctionContext.CronOccurrenceOperations;
        FunctionName = jobFunctionContext.FunctionName;
    }

    public TRequest Request { get; set; }
}

public class JobFunctionContext
{
    internal AsyncServiceScope ServiceScope { get; set; }
    public required Action RequestCancelOperationAction { get; init; }
    public Guid Id { get; internal set; }
    public JobType Type { get; internal set; }
    public int RetryCount { get; internal set; }
    public bool IsDue { get; internal set; }

    /// <summary>
    /// The time this job was scheduled to run (UTC).
    /// For time jobs, this is the ExecutionTime; for cron, the occurrence ExecutionTime.
    /// </summary>
    public DateTime ScheduledFor { get; internal set; }
    public required string FunctionName { get; init; }
    public required CronOccurrenceOperations CronOccurrenceOperations { get; init; }

    public void RequestCancellation() => RequestCancelOperationAction();

    internal void SetServiceScope(AsyncServiceScope serviceScope) => ServiceScope = serviceScope;
}

public class CronOccurrenceOperations
{
    public required Action SkipIfAlreadyRunningAction { get; init; }

    public void SkipIfAlreadyRunning() => SkipIfAlreadyRunningAction();
}
