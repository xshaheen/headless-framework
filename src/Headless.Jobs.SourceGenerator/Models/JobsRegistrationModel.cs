// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>The complete input of the emitter for one assembly. Contains no locations or diagnostics.</summary>
internal sealed record JobsRegistrationModel(
    string AssemblyName,
    EquatableArray<JobFunctionModel> Functions,
    EquatableArray<MiddlewareRegistrationModel> Middleware,
    EquatableArray<string> ConflictingTypeNames
);

/// <summary>
/// The combined outcome for one assembly. <see cref="Model"/> is <see langword="null"/> when nothing may be emitted,
/// such as when function names or request types collide.
/// </summary>
internal sealed record JobsGenerationResult(JobsRegistrationModel? Model, EquatableArray<DiagnosticInfo> Diagnostics)
{
    public static JobsGenerationResult Empty { get; } = new(Model: null, EquatableArray<DiagnosticInfo>.Empty);
}
