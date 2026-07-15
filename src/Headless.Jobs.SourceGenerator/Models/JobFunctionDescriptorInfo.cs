// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.SourceGenerator.Models;

internal sealed class JobFunctionDescriptorInfo(
    string functionName,
    string? requestTypeName,
    string cronExpression,
    int priority,
    int maxConcurrency
)
{
    public string FunctionName { get; } = functionName;

    public string? RequestTypeName { get; } = requestTypeName;

    public string CronExpression { get; } = cronExpression;

    public int Priority { get; } = priority;

    public int MaxConcurrency { get; } = maxConcurrency;
}
