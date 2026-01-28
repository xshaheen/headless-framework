using System.Diagnostics;
using Headless.Ticker.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Ticker.Instrumentation;

/// <summary>
/// No-operation implementation of ITickerQInstrumentation
/// </summary>
public sealed class LoggerInstrumentation : TickerQBaseLoggerInstrumentation, ITickerQInstrumentation
{
    public LoggerInstrumentation(ILogger<LoggerInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder)
        : base(logger, optionsBuilder.NodeIdentifier) { }

    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context) => null;
}
