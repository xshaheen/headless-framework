using Framework.Ticker.Utilities.Enums;

namespace Framework.Ticker.Base;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TickerFunctionAttribute : Attribute
{
    public TickerFunctionAttribute(
        string functionName,
        string? cronExpression = null,
        TickerTaskPriority taskPriority = TickerTaskPriority.Normal
    )
    {
        _ = functionName;
        _ = cronExpression;
        _ = taskPriority;
    }

    public TickerFunctionAttribute(string functionName, TickerTaskPriority taskPriority)
    {
        _ = functionName;
        _ = taskPriority;
    }
}
