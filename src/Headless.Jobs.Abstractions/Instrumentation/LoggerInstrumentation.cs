using System.Diagnostics;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Instrumentation;

/// <summary>
/// No-operation implementation of IJobsInstrumentation
/// </summary>
public sealed class LoggerInstrumentation : JobsBaseLoggerInstrumentation, IJobsInstrumentation
{
    public LoggerInstrumentation(ILogger<LoggerInstrumentation> logger, SchedulerOptionsBuilder optionsBuilder)
        : base(logger, optionsBuilder.NodeIdentifier) { }

    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context) => null;
}
