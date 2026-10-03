// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.SourceGenerator.Models;
using Headless.Jobs.SourceGenerator.Utilities;
using Headless.Jobs.SourceGenerator.Validation;
using Headless.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Headless.Jobs.SourceGenerator.Parsing;

/// <summary>The values of one <c>[Job]</c> attribute application, read without interpreting them.</summary>
/// <remarks>
/// The policy symbol never leaves the parser: the job model keeps only its name, so incremental caching compares values.
/// </remarks>
internal sealed record JobAttributeValues(
    string? Identity,
    string? CronExpression,
    string? TimeZone,
    int Priority,
    int MaxConcurrency,
    int? OnMissedRun,
    int? MissedRunGraceSeconds,
    int? OnOverlap,
    string ContractVersion,
    ITypeSymbol? FailurePolicy
)
{
    public static JobAttributeValues Read(AttributeData attribute)
    {
        var identity =
            attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
        string? cronExpression = null;
        string? timeZone = null;
        var priority = SourceGeneratorConstants.NormalJobPriority;
        var maxConcurrency = 0;
        var contractVersion = SourceGeneratorConstants.InitialContractVersion;

        // The recovery knobs are read only when actually written. That distinguishes "unset" (fall through to the
        // scheduler-wide default at creation) from "explicitly set to the framework default", which the property
        // getters cannot express because attribute arguments cannot be nullable value types.
        int? onMissedRun = null;
        int? missedRunGraceSeconds = null;
        int? onOverlap = null;
        ITypeSymbol? failurePolicy = null;

        foreach (var named in attribute.NamedArguments)
        {
            var value = named.Value.Value;
            switch (named.Key)
            {
                case "Cron":
                    cronExpression = value as string;
                    break;
                case "TimeZone":
                    timeZone = value as string;
                    break;
                case "Priority" when value is int priorityValue:
                    priority = priorityValue;
                    break;
                case "MaxConcurrency" when value is int concurrencyValue:
                    maxConcurrency = concurrencyValue;
                    break;
                case "ContractVersion":
                    contractVersion = value as string ?? string.Empty;
                    break;
                case "OnMissedRun" when value is int missedRunValue:
                    onMissedRun = missedRunValue;
                    break;
                case "MissedRunGraceSeconds" when value is int graceValue:
                    missedRunGraceSeconds = graceValue;
                    break;
                case "OnOverlap" when value is int overlapValue:
                    onOverlap = overlapValue;
                    break;
                // An unresolved type is already a compiler error at the attribute, so it is not reported again.
                case "FailurePolicy" when value is ITypeSymbol { TypeKind: not TypeKind.Error } policyValue:
                    failurePolicy = policyValue;
                    break;
            }
        }

        return new(
            identity,
            cronExpression,
            timeZone,
            priority,
            maxConcurrency,
            onMissedRun,
            missedRunGraceSeconds,
            onOverlap,
            contractVersion,
            failurePolicy
        );
    }
}
