// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>Everything the emitter needs about one <c>[JobFunction]</c> method, captured as values.</summary>
/// <param name="RequestTypeName">
/// The fully qualified <c>T</c> of a <c>JobFunctionContext&lt;T&gt;</c> parameter, or <see langword="null"/> for a
/// requestless function.
/// </param>
internal sealed record JobFunctionModel(
    JobClassModel Class,
    string MethodName,
    bool IsStaticMethod,
    bool IsAwaitable,
    EquatableArray<string> InvocationArguments,
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
    public bool UsesGenericContext => RequestTypeName is not null;
}

/// <summary>The class that declares a job function, including how the generated factory constructs it.</summary>
/// <param name="TypeName">
/// The fully qualified (<c>global::</c>) name, so generated code cannot be captured by the namespace it is emitted into.
/// </param>
internal sealed record JobClassModel(
    string TypeName,
    string FactoryMethodName,
    bool IsStatic,
    EquatableArray<ConstructorParameterModel> ConstructorParameters
);

/// <summary>
/// One constructor argument. <see cref="TypeName"/> is <see langword="null"/> for the <c>serviceProvider</c>
/// parameter, which is passed through without resolution.
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
