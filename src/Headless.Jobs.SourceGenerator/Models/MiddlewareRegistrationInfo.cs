// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.SourceGenerator.Models;

internal sealed class MiddlewareRegistrationInfo(
    string typeName,
    string identity,
    string? function,
    int priority,
    bool isSchedule
)
{
    public string TypeName { get; } = typeName;
    public string Identity { get; } = identity;
    public string? Function { get; } = function;
    public int Priority { get; } = priority;
    public bool IsSchedule { get; } = isSchedule;
}
