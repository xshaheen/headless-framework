using Headless.Ticker.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Ticker.Base;

public class TickerFunctionContext<TRequest> : TickerFunctionContext
{
    public TickerFunctionContext(TickerFunctionContext tickerFunctionContext, TRequest request)
    {
        Request = request;
        Id = tickerFunctionContext.Id;
        Type = tickerFunctionContext.Type;
        RetryCount = tickerFunctionContext.RetryCount;
        IsDue = tickerFunctionContext.IsDue;
        ScheduledFor = tickerFunctionContext.ScheduledFor;
        RequestCancelOperationAction = tickerFunctionContext.RequestCancelOperationAction;
        CronOccurrenceOperations = tickerFunctionContext.CronOccurrenceOperations;
        FunctionName = tickerFunctionContext.FunctionName;
    }

    public TRequest Request { get; set; }
}

public class TickerFunctionContext
{
    internal AsyncServiceScope ServiceScope { get; set; }
    public required Action RequestCancelOperationAction { get; init; }
    public Guid Id { get; internal set; }
    public TickerType Type { get; internal set; }
    public int RetryCount { get; internal set; }
    public bool IsDue { get; internal set; }

    /// <summary>
    /// The time this ticker was scheduled to run (UTC).
    /// For time tickers, this is the ExecutionTime; for cron, the occurrence ExecutionTime.
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
