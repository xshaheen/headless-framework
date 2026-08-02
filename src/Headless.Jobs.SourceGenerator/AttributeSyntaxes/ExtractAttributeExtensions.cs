// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;

namespace Headless.Jobs.SourceGenerator.AttributeSyntaxes;

internal static class ExtractAttributeExtensions
{
    public static (
        string? functionName,
        string? cronExpression,
        int taskPriority,
        int maxConcurrency,
        int? onMissedRun,
        int? missedRunGraceSeconds
    ) GetJobFunctionAttributeValues(this AttributeData attrData)
    {
        // If for some reason there is no ctor (should be rare), return defaults
        var ctor = attrData.AttributeConstructor;
        if (ctor == null)
        {
            return (null, null, 0, 0, null, null);
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

        // The recovery knobs are attribute PROPERTIES, not constructor parameters, so they arrive as named arguments.
        // Reading them only when actually written is what distinguishes "unset" (fall through to the scheduler-wide
        // default at creation) from "explicitly set to the framework default" — the property getters cannot express
        // that difference because attribute arguments cannot be nullable value types.
        int? onMissedRun = null;
        int? missedRunGraceSeconds = null;

        foreach (var named in attrData.NamedArguments)
        {
            switch (named.Key)
            {
                case "OnMissedRun":
                    if (named.Value.Value is int policyValue)
                    {
                        onMissedRun = policyValue;
                    }
                    break;
                case "MissedRunGraceSeconds":
                    if (named.Value.Value is int graceValue)
                    {
                        missedRunGraceSeconds = graceValue;
                    }
                    break;
            }
        }

        return (functionName, cronExpression, taskPriority, maxConcurrency, onMissedRun, missedRunGraceSeconds);
    }
}
