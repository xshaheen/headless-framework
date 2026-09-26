// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>Everything the emitter needs about one <c>[JobFunction]</c> method, captured as values.</summary>
internal sealed record JobFunctionModel(
    JobClassModel Class,
    string MethodName,
    bool IsStaticMethod,
    bool IsAwaitable,
    EquatableArray<string> InvocationArguments,
    string GenericTypeName,
    string? RequestTypeName,
    string? FunctionName,
    string? CronExpression,
    int Priority,
    int MaxConcurrency,
    int? OnMissedRun,
    int? MissedRunGraceSeconds,
    int? OnOverlap,
    string ContractVersion
)
{
    public bool UsesGenericContext => GenericTypeName.Length != 0;
}

/// <summary>The class that declares a job function, including how the generated factory constructs it.</summary>
internal sealed record JobClassModel(
    string FullName,
    string Namespace,
    string Name,
    bool IsStatic,
    EquatableArray<ConstructorParameterModel> ConstructorParameters
);

/// <summary>
/// One constructor argument. <see cref="TypeName"/> is <see langword="null"/> when the argument is passed through
/// without resolution (the <c>serviceProvider</c> parameter or an untyped parameter).
/// </summary>
internal sealed record ConstructorParameterModel(string Name, string? TypeName, string? ServiceKey);

/// <summary>
/// The transform output for one <c>[JobFunction]</c> method: the emission model plus what is needed only for
/// validation, kept apart so a location change alone never invalidates emitted source.
/// </summary>
internal sealed record JobFunctionResult(
    JobFunctionModel Function,
    LocationInfo? AttributeLocation,
    EquatableArray<DiagnosticInfo> Diagnostics
);
