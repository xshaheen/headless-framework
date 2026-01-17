using Microsoft.CodeAnalysis;

namespace Framework.Ticker.SourceGenerator.AttributeSyntaxes;

public static class ExtractAttributeExtensions
{
    public static (string? functionName, string? cronExpression, int taskPriority) GetTickerFunctionAttributeValues(
        this AttributeData attrData
    )
    {
        // If for some reason there is no ctor (should be rare), return defaults
        var ctor = attrData.AttributeConstructor;
        if (ctor == null)
        {
            return (null, null, 0);
        }

        var parameters = ctor.Parameters;
        string? functionName = null;
        string? cronExpression = null;
        var taskPriority = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            // Use provided argument if present; otherwise fall back to the parameter's default value
            var value =
                i < attrData.ConstructorArguments.Length
                    ? attrData.ConstructorArguments[i].Value
                    : parameters[i].ExplicitDefaultValue;

            switch (parameters[i].Name)
            {
                case "functionName":
                    functionName = value as string;
                    break;
                case "cronExpression":
                    cronExpression = value as string;
                    break;
                case "taskPriority":
                    if (value is int intValue)
                    {
                        taskPriority = intValue;
                    }
                    break;
            }
        }

        return (functionName, cronExpression, taskPriority);
    }
}
