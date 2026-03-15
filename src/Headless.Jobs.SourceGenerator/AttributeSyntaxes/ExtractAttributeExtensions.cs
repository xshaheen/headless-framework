using Microsoft.CodeAnalysis;

namespace Headless.Jobs.SourceGenerator.AttributeSyntaxes;

public static class ExtractAttributeExtensions
{
    public static (
        string? functionName,
        string? cronExpression,
        int taskPriority,
        int maxConcurrency
    ) GetJobFunctionAttributeValues(this AttributeData attrData)
    {
        // If for some reason there is no ctor (should be rare), return defaults
        var ctor = attrData.AttributeConstructor;
        if (ctor == null)
        {
            return (null, null, 0, 0);
        }

        var parameters = ctor.Parameters;
        string? functionName = null;
        string? cronExpression = null;
        var taskPriority = 0;
        var maxConcurrency = 0;

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
                case "maxConcurrency":
                    if (value is int mcValue)
                    {
                        maxConcurrency = mcValue;
                    }
                    break;
            }
        }

        return (functionName, cronExpression, taskPriority, maxConcurrency);
    }
}
