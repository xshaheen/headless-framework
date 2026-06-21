// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Instrumentation;

/// <summary>
/// No-operation implementation of IJobsInstrumentation
/// </summary>
internal sealed class LoggerInstrumentation : JobsBaseLoggerInstrumentation, IJobsInstrumentation
{
    public LoggerInstrumentation(ILogger<LoggerInstrumentation> logger, IJobsOwnerIdentity ownerIdentity)
        : base(logger, ownerIdentity) { }

    public override Activity? StartJobActivity(string activityName, InternalFunctionContext context) => null;
}
