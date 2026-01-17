using System.Diagnostics;
using Framework.Ticker.Utilities.Models;
using Microsoft.Extensions.Logging;

namespace Framework.Ticker.Utilities.Instrumentation;

/// <summary>
/// No-operation implementation of ITickerQInstrumentation
/// </summary>
public sealed class LoggerInstrumentation : TickerQBaseLoggerInstrumentation, ITickerQInstrumentation
{
    public LoggerInstrumentation(ILogger<LoggerInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder)
        : base(logger, optionsBuilder.NodeIdentifier) { }

    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context) => null;
}
