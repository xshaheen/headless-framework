// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.SourceGenerator.Models;

internal sealed class JobFunctionDescriptorInfo
{
    public JobFunctionDescriptorInfo(
        string functionName,
        string? requestTypeName,
        string cronExpression,
        int priority,
        int maxConcurrency
    )
    {
        FunctionName = functionName;
        RequestTypeName = requestTypeName;
        CronExpression = cronExpression;
        Priority = priority;
        MaxConcurrency = maxConcurrency;
    }

    public string FunctionName { get; }

    public string? RequestTypeName { get; }

    public string CronExpression { get; }

    public int Priority { get; }

    public int MaxConcurrency { get; }
}
