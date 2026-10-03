// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Jobs.SourceGenerator.Models;

/// <summary>
/// The combined outcome for one assembly. <see cref="Model"/> is <see langword="null"/> when nothing may be emitted,
/// such as when job identities or argument types collide.
/// </summary>
internal sealed record JobsGenerationResult(JobsRegistrationModel? Model, EquatableArray<DiagnosticInfo> Diagnostics)
{
    public static JobsGenerationResult Empty { get; } = new(Model: null, EquatableArray<DiagnosticInfo>.Empty);
}
