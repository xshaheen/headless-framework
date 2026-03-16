using Headless.Jobs.Enums;

namespace Headless.Jobs.Base;

[AttributeUsage(AttributeTargets.Method)]
public sealed class JobFunctionAttribute : Attribute
{
    public JobFunctionAttribute(
        string functionName,
        string? cronExpression = null,
        JobPriority taskPriority = JobPriority.Normal,
        int maxConcurrency = 0
    )
    {
        _ = functionName;
        _ = cronExpression;
        _ = taskPriority;
        _ = maxConcurrency;
    }

    public JobFunctionAttribute(string functionName, JobPriority taskPriority, int maxConcurrency = 0)
    {
        _ = functionName;
        _ = taskPriority;
        _ = maxConcurrency;
    }
}
